﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax
{
    internal partial struct Blender
    {
        private struct Reader
        {
            private readonly Lexer lexer;
            private Cursor oldTreeCursor;
            private ImmutableStack<TextChangeRange> changes;
            private int newPosition;
            private int changeDelta;
            private DirectiveStack newDirectives;
            private DirectiveStack oldDirectives;
            private LexerMode newLexerDrivenMode;

            public Reader(Blender blender)
            {
                this.lexer = blender.lexer;
                this.oldTreeCursor = blender.oldTreeCursor;
                this.changes = blender.changes;
                this.newPosition = blender.newPosition;
                this.changeDelta = blender.changeDelta;
                this.newDirectives = blender.newDirectives;
                this.oldDirectives = blender.oldDirectives;
                this.newLexerDrivenMode = blender.newLexerDrivenMode;
            }

            internal BlendedNode ReadNodeOrToken(LexerMode mode, bool asToken)
            {
                // This is the core driver of the blender.  It just sits in a loop trying to keep our
                // positions in the old and new text in sync.  When they're out of sync it will try
                // to match them back up, and it will apropriately determine which nodes or tokens
                // from the old tree can be reused as long as they don't overlap and changes or
                // contain any errors.

                while (true)
                {
                    // If the cursor in the old tree is finished, then our choice is easy.  We just
                    // read from the new text.
                    if (this.oldTreeCursor.IsFinished)
                    {
                        return this.ReadNewToken(mode);
                    }

                    // If delta is non-zero then that means our positions in the respective text
                    // streams are not in sync.  This can be because of to reasons.  Either:
                    //
                    //   a) we're further ahead in the new text (i.e. 'changeDelta' is negative). We
                    //      should keep skipping tokens in the old text until we catch up.
                    //      TODO(cyrusn): We could actually be smarter here and skip whole nodes if
                    //      they're shorter than the changeDelta.  We can try doing that in the future.
                    //
                    //   b) we're further ahead in the old text (i.e. 'changeDelta' is positive).
                    //      This can happen when we are skipping over portions of the old tree because
                    //      it overlapped with changed text spans. In this case, we want to read a
                    //      token to try to consume that changed text and ensure that we get synced up.
                    if (this.changeDelta < 0)
                    {
                        // Case '1' above.  We're behind in the old text, so move forward a token.
                        // And try again.
                        this.SkipOldToken();
                    }
                    else if (this.changeDelta > 0)
                    {
                        // Case '2' above.  We're behind in the new text, so read a token to try to
                        // catch up.
                        return this.ReadNewToken(mode);
                    }
                    else
                    {
                        // Attempt to take a node or token from the old tree.  If we can't, then
                        // either break down the current node we're looking at to its first child
                        // and try again, or move to the next token.
                        BlendedNode blendedNode;
                        if (this.TryTakeOldNodeOrToken(asToken, out blendedNode))
                        {
                            return blendedNode;
                        }

                        // Couldn't take the current node or token.  Figure out the next node or
                        // token to reconsider and try again.
                        if (this.oldTreeCursor.CurrentNodeOrToken.IsNode)
                        {
                            // It was a node.  Just move to its first token and try again.
                            this.oldTreeCursor = this.oldTreeCursor.MoveToFirstChild();
                        }
                        else
                        {
                            // It was a token, just move to the next token.
                            this.SkipOldToken();
                        }
                    }
                }
            }

            private void SkipOldToken()
            {
                Debug.Assert(!this.oldTreeCursor.IsFinished);

                // First, move down so that we're actually pointing at a token.  If we're already
                // pointing at a token, then we'll just stay there.
                this.oldTreeCursor = this.oldTreeCursor.MoveToFirstToken();
                var node = this.oldTreeCursor.CurrentNodeOrToken;

                // Now, skip past it.
                this.changeDelta += node.FullWidth;
                this.oldDirectives = node.ApplyDirectives(this.oldDirectives);
                this.oldTreeCursor = this.oldTreeCursor.MoveToNextSibling();

                // If our cursor is now after any changes, then just skip past them while upping
                // the changeDelta length.  This will let us know that we need to read tokens
                // from the new text to try to sync up.
                this.SkipPastChanges();
            }

            private void SkipPastChanges()
            {
                var oldPosition = this.oldTreeCursor.CurrentNodeOrToken.Position;
                while (!this.changes.IsEmpty && oldPosition >= this.changes.Peek().Span.End)
                {
                    var change = this.changes.Peek();

                    this.changes = this.changes.Pop();
                    this.changeDelta += change.NewLength - change.Span.Length;
                }
            }

            private BlendedNode ReadNewToken(LexerMode mode)
            {
                Debug.Assert(this.changeDelta > 0 || this.oldTreeCursor.IsFinished);

                // The new text is either behind the cursor, or the cursor is done.  In either event,
                // we need to lex a real token from the stream.
                var token = this.LexNewToken(mode);

                // If the oldTreeCursor was finished, then the below code isn't really necessary.
                // We'll just repeat the outer reader loop and call right back into ReadNewToken.
                // That will then call LexNewToken (which doesn't use either of these variables).  If
                // oldTreeCursor wasn't finished then we need to update our state based on the token
                // we just read.
                var width = token.FullWidth;
                this.newPosition += width;
                this.changeDelta -= width;

                // By reading a token we may either have read into, or past, change ranges.  Skip
                // past them.  This will increase changeDelta which will indicate to us that we need
                // to keep on lexing.
                this.SkipPastChanges();

                return this.CreateBlendedNode(node: null, token: token);
            }

            private SyntaxToken LexNewToken(LexerMode mode)
            {
                if (this.lexer.TextWindow.Position != this.newPosition)
                {
                    this.lexer.Reset(this.newPosition, this.newDirectives);
                }

                if (mode >= LexerMode.XmlDocComment)
                {
                    mode |= this.newLexerDrivenMode;
                }

                var token = this.lexer.Lex(ref mode);
                this.newDirectives = this.lexer.Directives;
                this.newLexerDrivenMode = mode & (LexerMode.MaskXmlDocCommentLocation | LexerMode.MaskXmlDocCommentStyle);
                return token;
            }

            private bool TryTakeOldNodeOrToken(
                bool asToken,
                out BlendedNode blendedNode)
            {
                // If we're asking for tokens, then first move down to our first token.  (if we're
                // already at a token, then this won't do anything).
                if (asToken)
                {
                    this.oldTreeCursor = this.oldTreeCursor.MoveToFirstToken();
                }

                // See if we're actually able to reuse this node or token.  If not, our caller will
                // move the cursor to the next appropriate position and will try again.
                var currentNodeOrToken = this.oldTreeCursor.CurrentNodeOrToken;
                if (!CanReuse(currentNodeOrToken))
                {
                    blendedNode = default(BlendedNode);
                    return false;
                }

                // We can reuse this node or token.  Move us forward in the new text, and move to the
                // next sibling.
                this.newPosition += currentNodeOrToken.FullWidth;
                this.oldTreeCursor = this.oldTreeCursor.MoveToNextSibling();

                this.newDirectives = currentNodeOrToken.ApplyDirectives(this.newDirectives);
                this.oldDirectives = currentNodeOrToken.ApplyDirectives(this.oldDirectives);

                blendedNode = CreateBlendedNode(
                    node: (CSharp.CSharpSyntaxNode)currentNodeOrToken.AsNode(),
                    token: (InternalSyntax.SyntaxToken)currentNodeOrToken.AsToken().Node);
                return true;
            }

            private bool CanReuse(SyntaxNodeOrToken nodeOrToken)
            {
                // Zero width nodes and tokens always indicate that the parser had to do
                // something tricky, so don't reuse them.
                // NOTE: this is slightly different from IsMissing because of omitted type arguments
                // and array size expressions.
                if (nodeOrToken.FullWidth == 0)
                {
                    return false;
                }

                // As of 2013/03/14, the compiler never attempts to incrementally parse a tree containing
                // annotations.  Our goal in instituting this restriction is to prevent API clients from
                // taking a depedency on the survival of annotations.
                if (nodeOrToken.ContainsAnnotations)
                {
                    return false;
                }

                // We can't reuse a node or token if it intersects a changed text range.
                if (this.IntersectsNextChange(nodeOrToken))
                {
                    return false;
                }

                // don't reuse nodes or tokens with skipped text or diagnostics attached to them
                if (nodeOrToken.ContainsDiagnostics ||
                    (nodeOrToken.IsToken && ((CSharpSyntaxNode)nodeOrToken.AsToken().Node).ContainsSkippedText && nodeOrToken.Parent.ContainsDiagnostics))
                {
                    return false;
                }

                // fabricated tokens did not come from the lexer (likely from parser)
                if (IsFabricatedToken(nodeOrToken.CSharpKind()))
                {
                    return false;
                }

                // don't reuse nodes that are incomplete. this helps cases were an incomplete node
                // completes differently after a change with far look-ahead.
                //
                // NOTE(cyrusn): It is very unfortunate that we even need this check given that we
                // have already checked for ContainsDiagnostics above.  However, there is a case where we
                // can have a node with a missing token *and* there are no diagnostics.
                // Specifically, this happens in the REPL when you have the last statement without a
                // trailing semicolon.  We treat this as an ExpressionStatement with a missing
                // semicolon, but we do not report errors.  It would be preferable to fix that so
                // that the semicolon can be optional rather than abusing the system.
                if ((nodeOrToken.IsToken && nodeOrToken.AsToken().IsMissing) ||
                    (nodeOrToken.IsNode && IsIncomplete((CSharp.CSharpSyntaxNode)nodeOrToken.AsNode())))
                {
                    return false;
                }

                if (!nodeOrToken.ContainsDirectives)
                {
                    return true;
                }

                return this.newDirectives.IncrementallyEquivalent(this.oldDirectives);
            }

            private bool IntersectsNextChange(SyntaxNodeOrToken nodeOrToken)
            {
                if (this.changes.IsEmpty)
                {
                    return false;
                }

                var oldSpan = nodeOrToken.FullSpan;
                var changeSpan = this.changes.Peek().Span;

                // if old node intersects effective range of the change, we cannot use it
                return oldSpan.IntersectsWith(changeSpan);
            }

            private static bool IsIncomplete(CSharp.CSharpSyntaxNode node)
            {
                // A node is incomplete if the last token in it is a missing token.  Use the green
                // node to determine this as it's much faster than going through the red API.
                return node.Green.GetLastTerminal().IsMissing;
            }

            // any token that was fabricated by the parser
            private static bool IsFabricatedToken(SyntaxKind kind)
            {
                switch (kind)
                {
                    case SyntaxKind.GreaterThanGreaterThanToken:
                    case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                        return true;
                    default:
                        return SyntaxFacts.IsContextualKeyword(kind);
                }
            }

            private BlendedNode CreateBlendedNode(CSharp.CSharpSyntaxNode node, SyntaxToken token)
            {
                return new BlendedNode(node, token,
                    new Blender(lexer, oldTreeCursor, changes, newPosition, changeDelta, newDirectives, oldDirectives, newLexerDrivenMode));
            }
        }
    }
}
