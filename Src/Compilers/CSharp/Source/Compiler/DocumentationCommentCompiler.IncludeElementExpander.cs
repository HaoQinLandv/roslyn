// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    // Traverses the symbol table processing XML documentation comments and optionally writing them to
    // a provided stream.
    internal partial class DocumentationCommentCompiler : CSharpSymbolVisitor
    {
        private class IncludeElementExpander
        {
            private readonly Symbol memberSymbol;
            private readonly ImmutableArray<CSharpSyntaxNode> sourceIncludeElementNodes;
            private readonly CSharpCompilation compilation;
            private readonly DiagnosticBag diagnostics;
            private readonly CancellationToken cancellationToken;

            private int nextSourceIncludeElementIndex;
            private HashSet<Location> inProgressIncludeElementNodes;
            private HashSet<ParameterSymbol> documentedParameters;
            private HashSet<TypeParameterSymbol> documentedTypeParameters;
            private DocumentationCommentIncludeCache includedFileCache;

            private IncludeElementExpander(
                Symbol memberSymbol,
                ImmutableArray<CSharpSyntaxNode> sourceIncludeElementNodes,
                CSharpCompilation compilation,
                HashSet<ParameterSymbol> documentedParameters,
                HashSet<TypeParameterSymbol> documentedTypeParameters,
                DocumentationCommentIncludeCache includedFileCache,
                DiagnosticBag diagnostics,
                CancellationToken cancellationToken)
            {
                this.memberSymbol = memberSymbol;
                this.sourceIncludeElementNodes = sourceIncludeElementNodes;
                this.compilation = compilation;
                this.diagnostics = diagnostics;
                this.cancellationToken = cancellationToken;

                this.documentedParameters = documentedParameters;
                this.documentedTypeParameters = documentedTypeParameters;
                this.includedFileCache = includedFileCache;

                this.nextSourceIncludeElementIndex = 0;
            }

            public static void ProcessIncludes(
                string unprocessed,
                Symbol memberSymbol,
                ImmutableArray<CSharpSyntaxNode> sourceIncludeElementNodes,
                CSharpCompilation compilation,
                ref HashSet<ParameterSymbol> documentedParameters,
                ref HashSet<TypeParameterSymbol> documentedTypeParameters,
                ref DocumentationCommentIncludeCache includedFileCache,
                TextWriter writer,
                DiagnosticBag diagnostics,
                CancellationToken cancellationToken)
            {
                // If there are no include elements, then there's nothing to expand.
                // NOTE: By skipping parsing and re-writing, we avoid slightly
                // modifying the whitespace, as we would if we let the XmlWriter
                // do the writing.  This saves us a lot of work in the common case
                // but slightly reduces consistency when include elements are
                // present.
                if (sourceIncludeElementNodes.IsEmpty)
                {
                    if (writer != null)
                    {
                        writer.Write(unprocessed);
                    }
                    return;
                }

                XDocument doc;

                try
                {
                    // NOTE: XDocument.Parse seems to do a better job of preserving whitespace
                    // than XElement.Parse.
                    doc = XDocument.Parse(unprocessed, LoadOptions.PreserveWhitespace);
                }
                catch (XmlException e)
                {
                    // If one of the trees wasn't diagnosing doc comments, then an error might have slipped through.
                    // Otherwise, we shouldn't see exceptions from XDocument.Parse.
                    Debug.Assert(sourceIncludeElementNodes.All(syntax => syntax.SyntaxTree.Options.DocumentationMode < DocumentationMode.Diagnose),
                        "Why didn't our parser catch this exception? " + e);
                    if (writer != null)
                    {
                        writer.Write(unprocessed);
                    }
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                IncludeElementExpander expander = new IncludeElementExpander(
                    memberSymbol,
                    sourceIncludeElementNodes,
                    compilation,
                    documentedParameters,
                    documentedTypeParameters,
                    includedFileCache,
                    diagnostics,
                    cancellationToken);

                foreach (XNode node in expander.Rewrite(doc, currentXmlFilePath: null, originatingSyntax: null))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (writer != null)
                    {
                        writer.Write(node);
                    }
                }

                Debug.Assert(expander.nextSourceIncludeElementIndex == expander.sourceIncludeElementNodes.Length);

                documentedParameters = expander.documentedParameters;
                documentedTypeParameters = expander.documentedTypeParameters;
                includedFileCache = expander.includedFileCache;
            }

            /// <remarks>
            /// Rewrites nodes in <paramref name="nodes"/>, which is a snapshot of nodes from the original document.
            /// We're mutating the tree as we rewrite, so it's important to grab a snapshot of the
            /// nodes that we're going to reparent before we enumerate them.
            /// </remarks>
            private XNode[] RewriteMany(XNode[] nodes, string currentXmlFilePath, CSharpSyntaxNode originatingSyntax)
            {
                Debug.Assert(nodes != null);

                ArrayBuilder<XNode> builder = null;
                foreach (XNode child in nodes)
                {
                    if (builder == null)
                    {
                        builder = ArrayBuilder<XNode>.GetInstance();
                    }

                    builder.AddRange(Rewrite(child, currentXmlFilePath, originatingSyntax));
                }

                // Nodes returned by this method are going to be attached to a new parent, so it's
                // important that they don't already have parents.  If a node with a parent is
                // attached to a new parent, it is copied and its annotations are dropped.
                Debug.Assert(builder == null || builder.All(node => node.Parent == null));

                return builder == null ? SpecializedCollections.EmptyArray<XNode>() : builder.ToArrayAndFree();
            }

            // CONSIDER: could add a depth count and just not rewrite below that depth.
            private XNode[] Rewrite(XNode node, string currentXmlFilePath, CSharpSyntaxNode originatingSyntax)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string commentMessage = null;

                if (node.NodeType == XmlNodeType.Element)
                {
                    XElement element = (XElement)node;
                    if (ElementNameIs(element, DocumentationCommentXmlNames.IncludeElementName))
                    {
                        XNode[] rewritten = RewriteIncludeElement(element, currentXmlFilePath, originatingSyntax, out commentMessage);
                        if (rewritten != null)
                        {
                            return rewritten;
                        }
                    }
                }

                XContainer container = node as XContainer;
                if (container == null)
                {
                    Debug.Assert(commentMessage == null, "How did we get an error comment for a non-container?");
                    return new XNode[] { node.Copy(copyAttributeAnnotations: false) };
                }

                IEnumerable<XNode> oldNodes = container.Nodes();

                // Do this after grabbing the nodes, so we don't see copies of them.
                container = container.Copy(copyAttributeAnnotations: false);

                // WARN: don't use node after this point - use container since it's already been copied.

                if (oldNodes != null)
                {
                    XNode[] rewritten = RewriteMany(oldNodes.ToArray(), currentXmlFilePath, originatingSyntax);
                    container.ReplaceNodes(rewritten);
                }

                // NOTE: we may modify the values of cref attributes, so don't do this until AFTER we've
                // made a copy.  Also, we only care if we're included text - otherwise we've already 
                // processed the cref.
                if (container.NodeType == XmlNodeType.Element && originatingSyntax != null)
                {
                    XElement element = (XElement)container;
                    foreach (XAttribute attribute in element.Attributes())
                    {
                        if (AttributeNameIs(attribute, DocumentationCommentXmlNames.CrefAttributeName))
                        {
                            BindAndReplaceCref(attribute, originatingSyntax);
                        }
                        else if (AttributeNameIs(attribute, DocumentationCommentXmlNames.NameAttributeName))
                        {
                            if (ElementNameIs(element, DocumentationCommentXmlNames.ParameterElementName) ||
                                ElementNameIs(element, DocumentationCommentXmlNames.ParameterReferenceElementName))
                            {
                                BindName(attribute, originatingSyntax, isParameter: true);
                            }
                            else if (ElementNameIs(element, DocumentationCommentXmlNames.TypeParameterElementName) ||
                                ElementNameIs(element, DocumentationCommentXmlNames.TypeParameterReferenceElementName))
                            {
                                BindName(attribute, originatingSyntax, isParameter: false);
                            }
                        }
                    }
                }

                if (commentMessage == null)
                {
                    return new XNode[] { container }; // Already copied.
                }
                else
                {
                    XComment failureComment = new XComment(commentMessage);
                    return new XNode[] { failureComment, container }; // Already copied.
                }
            }

            private static bool ElementNameIs(XElement element, string name)
            {
                return string.IsNullOrEmpty(element.Name.NamespaceName) && DocumentationCommentXmlNames.ElementEquals(element.Name.LocalName, name);
            }

            private static bool AttributeNameIs(XAttribute attribute, string name)
            {
                return string.IsNullOrEmpty(attribute.Name.NamespaceName) && DocumentationCommentXmlNames.AttributeEquals(attribute.Name.LocalName, name);
            }

            /// <remarks>
            /// This method boils down to Rewrite(XDocument.Load(fileAttrValue).XPathSelectElements(pathAttrValue)).  
            /// Everything else is error handling.
            /// </remarks>
            private XNode[] RewriteIncludeElement(XElement includeElement, string currentXmlFilePath, CSharpSyntaxNode originatingSyntax, out string commentMessage)
            {
                Location location = GetIncludeElementLocation(includeElement, ref currentXmlFilePath, ref originatingSyntax);
                Debug.Assert(originatingSyntax != null);

                bool diagnose = originatingSyntax.SyntaxTree.ReportDocumentationCommentDiagnostics();

                if (!EnterIncludeElement(location))
                {
                    // NOTE: these must exist since we're already processed this node elsewhere in the call stack.
                    XAttribute fileAttr = includeElement.Attribute(XName.Get(DocumentationCommentXmlNames.FileAttributeName));
                    XAttribute pathAttr = includeElement.Attribute(XName.Get(DocumentationCommentXmlNames.PathAttributeName));
                    string filePathValue = fileAttr.Value;
                    string xpathValue = pathAttr.Value;

                    if (diagnose)
                    {
                        diagnostics.Add(ErrorCode.WRN_FailedInclude, location, filePathValue, xpathValue, CSharpResources.OperationCausedStackOverflow);
                    }

                    // TODO: use culture from compilation instead of invariant culture?
                    commentMessage = ErrorFacts.GetMessage(MessageID.IDS_XMLNOINCLUDE, CultureInfo.InvariantCulture);

                    // Don't inspect the children - we're already in a cycle.
                    return new XNode[] { new XComment(commentMessage), includeElement.Copy(copyAttributeAnnotations: false) };
                }

                DiagnosticBag includeDiagnostics = DiagnosticBag.GetInstance();

                try
                {
                    XAttribute fileAttr = includeElement.Attribute(XName.Get(DocumentationCommentXmlNames.FileAttributeName));
                    XAttribute pathAttr = includeElement.Attribute(XName.Get(DocumentationCommentXmlNames.PathAttributeName));

                    bool hasFileAttribute = fileAttr != null;
                    bool hasPathAttribute = pathAttr != null;
                    if (!hasFileAttribute || !hasPathAttribute)
                    {
                        var subMessage = hasFileAttribute ? MessageID.IDS_XMLMISSINGINCLUDEPATH.Localize() : MessageID.IDS_XMLMISSINGINCLUDEFILE.Localize();
                        includeDiagnostics.Add(ErrorCode.WRN_InvalidInclude, location, subMessage);
                        commentMessage = MakeCommentMessage(location, MessageID.IDS_XMLBADINCLUDE);
                        return null;
                    }

                    string xpathValue = pathAttr.Value;
                    string filePathValue = fileAttr.Value;

                    string resolvedFilePath = compilation.Options.FileResolver.ResolveXmlFile(filePathValue, currentXmlFilePath);

                    if (resolvedFilePath == null)
                    {
                        // NOTE: same behavior as IOException.
                        includeDiagnostics.Add(ErrorCode.WRN_FailedInclude, location, filePathValue, xpathValue, new FileNotFoundException().Message);
                        commentMessage = MakeCommentMessage(location, MessageID.IDS_XMLFAILEDINCLUDE);
                        return null;
                    }

                    if (includedFileCache == null)
                    {
                        includedFileCache = new DocumentationCommentIncludeCache(compilation.Options.FileResolver);
                    }

                    try
                    {
                        XDocument doc;

                        try
                        {
                            doc = includedFileCache.GetOrMakeDocument(resolvedFilePath);
                        }
                        catch (IOException e)
                        {
                            // NOTE: same behavior as resolvedFilePath == null.
                            includeDiagnostics.Add(ErrorCode.WRN_FailedInclude, location, filePathValue, xpathValue, e.Message);
                            commentMessage = MakeCommentMessage(location, MessageID.IDS_XMLFAILEDINCLUDE);
                            return null;
                        }

                        Debug.Assert(doc != null);

                        XElement[] loadedElements;
                        try
                        {
                            var xpathResult = doc.XPathSelectElements(xpathValue);

                            // Throws InvalidOperationException if the result of the XPath is an XDocument:
                            loadedElements = (xpathResult != null) ? xpathResult.ToArray() : null;
                        }
                        catch (XPathException e)
                        {
                            includeDiagnostics.Add(ErrorCode.WRN_FailedInclude, location, filePathValue, xpathValue, e.Message);
                            commentMessage = MakeCommentMessage(location, MessageID.IDS_XMLFAILEDINCLUDE);
                            return null;
                        }
                        catch (InvalidOperationException e)
                        {
                            includeDiagnostics.Add(ErrorCode.WRN_FailedInclude, location, filePathValue, xpathValue, e.Message);

                            if (location.IsInSource)
                            {
                                commentMessage = ErrorFacts.GetMessage(MessageID.IDS_XMLFAILEDINCLUDE, CultureInfo.InvariantCulture);

                                // As in Dev11, return only the comment - drop the include element.
                                return new XNode[] { new XComment(commentMessage) };
                            }
                            else
                            {
                                commentMessage = null;
                                return SpecializedCollections.EmptyArray<XNode>();
                            }
                        }

                        if (loadedElements != null && loadedElements.Length > 0)
                        {
                            // change the current XML file path for nodes contained in the document:
                            XNode[] result = RewriteMany(loadedElements, resolvedFilePath, originatingSyntax);

                            // The elements could be rewritten away if they are includes that refer to invalid
                            // (but existing and accessible) XML files.  If this occurs, behave as if we
                            // had failed to find any XPath results (as in Dev11).
                            if (result.Length > 0)
                            {
                                // NOTE: in this case, we do NOT visit the children of the include element -
                                // they are dropped.
                                commentMessage = null;
                                return result;
                            }
                        }

                        commentMessage = MakeCommentMessage(location, MessageID.IDS_XMLNOINCLUDE);
                        return null;
                    }
                    catch (XmlException e)
                    {
                        // NOTE: invalid XML is handled differently from other errors - we don't include the include element
                        // in the results and the location is in the included (vs includING) file.

                        Location errorLocation = XmlLocation.Create(e, resolvedFilePath);
                        includeDiagnostics.Add(ErrorCode.WRN_XMLParseIncludeError, errorLocation, GetDescription(e)); //NOTE: location is in included file.

                        if (location.IsInSource)
                        {
                            commentMessage = string.Format(ErrorFacts.GetMessage(MessageID.IDS_XMLIGNORED2, CultureInfo.InvariantCulture), resolvedFilePath);

                            // As in Dev11, return only the comment - drop the include element.
                            return new XNode[] { new XComment(commentMessage) };
                        }
                        else
                        {
                            commentMessage = null;
                            return SpecializedCollections.EmptyArray<XNode>();
                        }
                    }
                }
                finally
                {
                    if (diagnose)
                    {
                        diagnostics.AddRange(includeDiagnostics);
                    }

                    includeDiagnostics.Free();

                    LeaveIncludeElement(location);
                }
            }

            private string MakeCommentMessage(Location location, MessageID messageId)
            {
                if (location.IsInSource)
                {
                    // TODO: use culture from compilation instead of invariant culture?
                    return ErrorFacts.GetMessage(messageId, CultureInfo.InvariantCulture);
                }
                else
                {
                    return null;
                }
            }

            private bool EnterIncludeElement(Location location)
            {
                if (this.inProgressIncludeElementNodes == null)
                {
                    this.inProgressIncludeElementNodes = new HashSet<Location>();
                }

                return this.inProgressIncludeElementNodes.Add(location);
            }

            private bool LeaveIncludeElement(Location location)
            {
                Debug.Assert(this.inProgressIncludeElementNodes != null);
                bool result = this.inProgressIncludeElementNodes.Remove(location);
                Debug.Assert(result);
                return result;
            }

            private Location GetIncludeElementLocation(XElement includeElement, ref string currentXmlFilePath, ref CSharpSyntaxNode originatingSyntax)
            {
                Location location = includeElement.Annotation<Location>();
                if (location != null)
                {
                    return location;
                }

                // If we are not in an XML file, then we must be in a source file.  Since we're traversing the XML tree in the same
                // order as the DocumentationCommentWalker, we can access the elements of includeElementNodes in order.
                if (currentXmlFilePath == null)
                {
                    Debug.Assert(nextSourceIncludeElementIndex < sourceIncludeElementNodes.Length);
                    Debug.Assert(originatingSyntax == null);
                    originatingSyntax = sourceIncludeElementNodes[nextSourceIncludeElementIndex];
                    location = originatingSyntax.Location;
                    nextSourceIncludeElementIndex++;

                    // #line shall not affect the base path:
                    currentXmlFilePath = location.GetLineSpan().Path;
                }
                else
                {
                    location = XmlLocation.Create(includeElement, currentXmlFilePath);
                }

                Debug.Assert(location != null);
                includeElement.AddAnnotation(location);
                return location;
            }

            private void BindAndReplaceCref(XAttribute attribute, CSharpSyntaxNode originatingSyntax)
            {
                string attributeValue = attribute.Value;
                CrefSyntax crefSyntax = SyntaxFactory.ParseCref(attributeValue);

                if (crefSyntax == null)
                {
                    // This can happen if the cref is verbatim (e.g. "T:C").
                    return;
                }

                // CONSIDER: It would be easy to construct an XmlLocation from the XAttribute, so that
                // we could point the user at the actual problem.
                Location sourceLocation = originatingSyntax.Location;

                RecordSyntaxDiagnostics(crefSyntax, sourceLocation); // Respects DocumentationMode.

                MemberDeclarationSyntax memberDeclSyntax = BinderFactory.GetAssociatedMemberForXmlSyntax(originatingSyntax);
                Debug.Assert(memberDeclSyntax != null,
                    "Why are we processing a documentation comment that is not attached to a member declaration?");

                Binder binder = BinderFactory.MakeCrefBinder(crefSyntax, memberDeclSyntax, compilation.GetBinderFactory(memberDeclSyntax.SyntaxTree));

                DiagnosticBag crefDiagnostics = DiagnosticBag.GetInstance();
                attribute.Value = GetDocumentationCommentId(crefSyntax, binder, crefDiagnostics); // NOTE: mutation (element must be a copy)
                RecordBindingDiagnostics(crefDiagnostics, sourceLocation); // Respects DocumentationMode.
                crefDiagnostics.Free();
            }

            private void BindName(XAttribute attribute, CSharpSyntaxNode originatingSyntax, bool isParameter)
            {
                XmlNameAttributeSyntax attrSyntax = ParseNameAttribute(attribute.ToString(), attribute.Parent.Name.LocalName);

                // CONSIDER: It would be easy to construct an XmlLocation from the XAttribute, so that
                // we could point the user at the actual problem.
                Location sourceLocation = originatingSyntax.Location;

                RecordSyntaxDiagnostics(attrSyntax, sourceLocation); // Respects DocumentationMode.

                MemberDeclarationSyntax memberDeclSyntax = BinderFactory.GetAssociatedMemberForXmlSyntax(originatingSyntax);
                Debug.Assert(memberDeclSyntax != null,
                    "Why are we processing a documentation comment that is not attached to a member declaration?");

                DiagnosticBag nameDiagnostics = DiagnosticBag.GetInstance();
                Binder binder = MakeNameBinder(isParameter, memberSymbol, compilation);
                DocumentationCommentCompiler.BindName(attrSyntax, binder, memberSymbol, ref documentedParameters, ref documentedTypeParameters, nameDiagnostics);
                RecordBindingDiagnostics(nameDiagnostics, sourceLocation); // Respects DocumentationMode.
                nameDiagnostics.Free();
            }


            // NOTE: We're not sharing code with the BinderFactory visitor, because we already have the
            // member symbol in hand, which makes things much easier.
            private static Binder MakeNameBinder(bool isParameter, Symbol memberSymbol, CSharpCompilation compilation)
            {
                Binder binder = new BuckStopsHereBinder(compilation);

                // All binders should have a containing symbol.
                Symbol containingSymbol = memberSymbol.ContainingSymbol;
                Debug.Assert((object)containingSymbol != null);
                binder = binder.WithContainingMemberOrLambda(containingSymbol);

                if (isParameter)
                {
                    ImmutableArray<ParameterSymbol> parameters = ImmutableArray<ParameterSymbol>.Empty;

                    switch (memberSymbol.Kind)
                    {
                        case SymbolKind.Method:
                            parameters = ((MethodSymbol)memberSymbol).Parameters;
                            break;
                        case SymbolKind.Property:
                            parameters = ((PropertySymbol)memberSymbol).Parameters;
                            break;
                        case SymbolKind.NamedType:
                        case SymbolKind.ErrorType:
                            NamedTypeSymbol typeSymbol = (NamedTypeSymbol)memberSymbol;
                            if (typeSymbol.IsDelegateType())
                            {
                                parameters = typeSymbol.DelegateInvokeMethod.Parameters;
                            }
                            break;
                    }

                    if (parameters.Length > 0)
                    {
                        binder = new WithParametersBinder(parameters, binder);
                    }
                }
                else
                {
                    switch (memberSymbol.Kind)
                    {
                        case SymbolKind.NamedType: // Includes delegates.
                        case SymbolKind.ErrorType:
                            NamedTypeSymbol typeSymbol = (NamedTypeSymbol)memberSymbol;
                            if (typeSymbol.Arity > 0)
                            {
                                binder = new WithClassTypeParametersBinder(typeSymbol, binder);
                            }
                            break;
                        case SymbolKind.Method:
                            MethodSymbol methodSymbol = (MethodSymbol)memberSymbol;
                            if (methodSymbol.Arity > 0)
                            {
                                binder = new WithMethodTypeParametersBinder(methodSymbol, binder);
                            }
                            break;
                    }
                }

                return binder;
            }

            private static XmlNameAttributeSyntax ParseNameAttribute(string attributeText, string elementName)
            {
                // NOTE: Rather than introducing a new code path that will have to be kept in 
                // sync with other mode changes distributed throughout Lexer, SyntaxParser, and 
                // DocumentationCommentParser, we'll just wrap the text in some lexable syntax
                // and then extract the piece we want.
                string commentText = string.Format(@"/// <{0} {1}/>", elementName, attributeText);

                SyntaxTriviaList leadingTrivia = SyntaxFactory.ParseLeadingTrivia(commentText, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose));
                Debug.Assert(leadingTrivia.Count == 1);
                SyntaxTrivia trivia = leadingTrivia.ElementAt(0);
                DocumentationCommentTriviaSyntax structure = (DocumentationCommentTriviaSyntax)trivia.GetStructure();
                Debug.Assert(structure.Content.Count == 2);
                XmlEmptyElementSyntax elementSyntax = (XmlEmptyElementSyntax)structure.Content[1];
                Debug.Assert(elementSyntax.Attributes.Count == 1);
                return (XmlNameAttributeSyntax)elementSyntax.Attributes[0];
            }

            /// <remarks>
            /// Respects the DocumentationMode at the source location.
            /// </remarks>
            private void RecordSyntaxDiagnostics(CSharpSyntaxNode treelessSyntax, Location sourceLocation)
            {
                if (treelessSyntax.ContainsDiagnostics && ((SyntaxTree)sourceLocation.SourceTree).ReportDocumentationCommentDiagnostics())
                {
                    // NOTE: treelessSyntax doesn't have its own SyntaxTree, so we have to access the diagnostics
                    // via the Dummy tree.
                    foreach (Diagnostic diagnostic in CSharpSyntaxTree.Dummy.GetDiagnostics(treelessSyntax))
                    {
                        diagnostics.Add(diagnostic.WithLocation(sourceLocation));
                    }
                }
            }

            /// <remarks>
            /// Respects the DocumentationMode at the source location.
            /// </remarks>
            private void RecordBindingDiagnostics(DiagnosticBag bindingDiagnostics, Location sourceLocation)
            {
                if (!bindingDiagnostics.IsEmptyWithoutResolution && ((SyntaxTree)sourceLocation.SourceTree).ReportDocumentationCommentDiagnostics())
                {
                    foreach (Diagnostic diagnostic in bindingDiagnostics.AsEnumerable())
                    {
                        // CONSIDER: Dev11 actually uses the originating location plus the offset into the cref/name,
                        // but that just seems silly.
                        diagnostics.Add(diagnostic.WithLocation(sourceLocation));
                    }
                }
            }
        }
    }
}