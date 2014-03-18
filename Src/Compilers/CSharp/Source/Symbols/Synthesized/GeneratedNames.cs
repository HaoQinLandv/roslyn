﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal static partial class GeneratedNames
    {
        internal const string TemporaryNamePrefix = "CS$";

        internal static bool IsGeneratedName(string memberName)
        {
            return memberName.Length > 0 && memberName[0] == '<';
        }

        internal static string MakeBackingFieldName(string propertyName)
        {
            return "<" + propertyName + ">k__BackingField";
        }

        internal static string MakeLambdaMethodName(string containingMethodName, int uniqueId)
        {
            return "<" + containingMethodName + ">b__" + uniqueId;
        }

        internal static string MakeAnonymousDisplayClassName(int uniqueId)
        {
            return "<>c__DisplayClass" + uniqueId;
        }

        internal static string MakeAnonymousTypeTemplateName(int index, int submissionSlotIndex, string moduleId)
        {
            var name = "<" + moduleId + ">f__AnonymousType" + index;
            if (submissionSlotIndex >= 0)
            {
                name += "#" + submissionSlotIndex;
            }
            return name;
        }

        internal const string AnonymousNamePrefix = "<>f__AnonymousType";

        internal static bool TryParseAnonymousTypeTemplateName(string name, out int index)
        {
            // No callers require anonymous types from net modules,
            // so names with module id are ignored.
            if (name.StartsWith(AnonymousNamePrefix, StringComparison.Ordinal))
            {
                if (int.TryParse(name.Substring(AnonymousNamePrefix.Length), out index))
                {
                    return true;
                }
            }

            index = -1;
            return false;
        }

        internal static string MakeAnonymousTypeBackingFieldName(string propertyName)
        {
            return "<" + propertyName + ">i__Field";
        }

        internal static string MakeAnonymousTypeParameterName(string propertyName)
        {
            return "<" + propertyName + ">j__TPar";
        }

        internal static bool TryParseAnonymousTypeParameterName(string typeParameterName, out string propertyName)
        {
            if (typeParameterName.StartsWith("<", StringComparison.Ordinal) &&
                typeParameterName.EndsWith(">j__TPar", StringComparison.Ordinal))
            {
                propertyName = typeParameterName.Substring(1, typeParameterName.Length - 9);
                return true;
            }

            propertyName = null;
            return false;
        }

        internal static string MakeIteratorOrAsyncDisplayClassName(string methodName, int uniqueId)
        {
            methodName = EnsureNoDotsInTypeName(methodName);
            return "<" + methodName + ">d__" + uniqueId;
        }

        private static string EnsureNoDotsInTypeName(string name)
        {
            // CLR generally allows names with dots, however some APIs like IMetaDataImport
            // can only return full type names combined with namespaces. 
            // see: http://msdn.microsoft.com/en-us/library/ms230143.aspx (IMetaDataImport::GetTypeDefProps)
            // When working with such APIs, names with dots become ambiguous since metadata 
            // consumer cannot figure where namespace ends and actual type name starts.
            // Therefore it is a good practice to avoid type names with dots.
            if (name.IndexOf('.') >= 0)
            {
                name = name.Replace('.', '_');
            }
            return name;
        }

        internal static string MakeFabricatedMethodName(int uniqueId)
        {
            return "<>n__FabricatedMethod" + uniqueId;
        }

        internal static string MakeLambdaDisplayClassLocalName(int uniqueId)
        {
            return TemporaryNamePrefix + "<>8__locals" + uniqueId;
        }

        internal static string MakeLambdaCacheName(int uniqueId)
        {
            return TemporaryNamePrefix + "<>9__CachedAnonymousMethodDelegate" + uniqueId;
        }

        internal static string MakeTemporaryName(TempKind kind, int uniqueId)
        {
            Debug.Assert((int)kind >= 0); // Negative kinds should not be named.

            // Matches names generated by Dev11.
            return string.Format(TemporaryNamePrefix + "{0}${1:0000}", (int)kind, uniqueId);
        }

        internal static bool TryParseTemporaryName(string name, out TempKind kind, out int uniqueId)
        {
            if (name.StartsWith(TemporaryNamePrefix, StringComparison.Ordinal))
            {
                name = name.Substring(TemporaryNamePrefix.Length);
                int separator = name.IndexOf('$');
                if (separator > 0)
                {
                    int k;
                    int n;
                    if (int.TryParse(name.Substring(0, separator), out k) && int.TryParse(name.Substring(separator + 1), out n))
                    {
                        kind = (TempKind)k;
                        uniqueId = n;
                        return true;
                    }
                }
            }

            kind = TempKind.None;
            uniqueId = 0;
            return false;
        }

        internal static string MakeFixedFieldImplementationName(string fieldName)
        {
            // the native compiler adds numeric digits at the end.  Roslyn does not.
            return "<" + fieldName + ">" + "e__FixedBuffer";
        }

        internal static string MakeStateMachineStateName()
        {
            return "<>1__state";
        }

        internal static bool TryParseIteratorName(string mangledTypeName, out string iteratorName)
        {
            if (mangledTypeName.Length < 3 || mangledTypeName[0] != '<')
            {
                iteratorName = null;
                return false;
            }

            int closing = mangledTypeName.IndexOf(">d__", 1, StringComparison.Ordinal);
            if (closing < 0)
            {
                iteratorName = null;
                return false;
            }

            iteratorName = mangledTypeName.Substring(1, closing - 1);
            return true;
        }

        internal static string MakeIteratorCurrentBackingFieldName()
        {
            return "<>2__current";
        }

        internal static string MakeIteratorCurrentThreadIdName()
        {
            return "<>l__initialThreadId";
        }

        internal static string MakeIteratorLocalName(string localName, int localNumber)
        {
            return "<" + localName + ">5__" + localNumber;
        }

        internal static string IteratorThisProxyName()
        {
            return "<>4__this";
        }

        internal static string IteratorParameterProxyName(string parameterName)
        {
            return "<>3__" + parameterName;
        }

        internal static string IteratorThisProxyProxyName()
        {
            return IteratorParameterProxyName(IteratorThisProxyName());
        }

        internal static string MakeDynamicCallSiteContainerName(string methodName, int uniqueId)
        {
            methodName = EnsureNoDotsInTypeName(methodName);

            return "<" + methodName + ">o__SiteContainer" + uniqueId;
        }

        internal static string MakeDynamicCallSiteFieldName(int uniqueId)
        {
            return "<>p__Site" + uniqueId;
        }

        internal static string AsyncBuilderName()
        {
            return "<>t__builder";
        }

        internal static string AsyncAwaiterFieldName(int number)
        {
            return "<>u__$awaiter" + number;
        }

        internal static string AsyncExceptionFieldName()
        {
            return "<>t__ex";
        }

        internal static string AsyncExprRetValueFieldName()
        {
            return "<>t__exprRetValue";
        }

        internal static string SpillTempName(int number)
        {
            return "<>7__wrap" + number;
        }
    }
}
