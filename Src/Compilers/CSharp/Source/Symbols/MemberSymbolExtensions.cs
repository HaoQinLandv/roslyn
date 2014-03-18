﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// SymbolExtensions for member symbols.
    /// </summary>
    internal static partial class SymbolExtensions
    {
        internal static bool HasParamsParameter(this Symbol member)
        {
            var @params = member.GetParameters();
            return !@params.IsEmpty && @params.Last().IsParams;
        }

        /// <summary>
        /// Get the parameters of a member symbol.  Should be a method, property, or event.
        /// </summary>
        internal static ImmutableArray<ParameterSymbol> GetParameters(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).Parameters;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).Parameters;
                case SymbolKind.Event:
                    return ImmutableArray<ParameterSymbol>.Empty;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    return ImmutableArray<ParameterSymbol>.Empty;
            }
        }

        /// <summary>
        /// Get the types of the parameters of a member symbol.  Should be a method, property, or event.
        /// </summary>
        internal static ImmutableArray<TypeSymbol> GetParameterTypes(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).ParameterTypes;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).ParameterTypes;
                case SymbolKind.Event:
                    return ImmutableArray<TypeSymbol>.Empty;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    return ImmutableArray<TypeSymbol>.Empty;
            }
        }

        internal static bool GetIsVararg(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).IsVararg;
                case SymbolKind.Property:
                case SymbolKind.Event:
                    return false;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    return false;
            }
        }

        /// <summary>
        /// Get the ref kinds of the parameters of a member symbol.  Should be a method, property, or event.
        /// </summary>
        internal static ImmutableArray<RefKind> GetParameterRefKinds(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).ParameterRefKinds;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).ParameterRefKinds;
                case SymbolKind.Event:
                    return ImmutableArray<RefKind>.Empty;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    return ImmutableArray<RefKind>.Empty;
            }
        }

        internal static int GetParameterCount(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).ParameterCount;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).ParameterCount;
                case SymbolKind.Event:
                    return 0;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    return 0;
            }
        }

        internal static bool HasUnsafeParameter(this Symbol member)
        {
            foreach (TypeSymbol parameterType in member.GetParameterTypes())
            {
                if (parameterType.IsUnsafe())
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAccessor(this MethodSymbol methodSymbol)
        {
            return (object)methodSymbol.AssociatedPropertyOrEvent != null;
        }

        public static bool IsAccessor(this Symbol symbol)
        {
            return symbol.Kind == SymbolKind.Method && IsAccessor((MethodSymbol)symbol);
        }

        public static bool IsIndexedPropertyAccessor(this MethodSymbol methodSymbol)
        {
            var propertyOrEvent = methodSymbol.AssociatedPropertyOrEvent;
            return ((object)propertyOrEvent != null) && propertyOrEvent.IsIndexedProperty();
        }

        public static bool IsOperator(this MethodSymbol methodSymbol)
        {
            return methodSymbol.MethodKind == MethodKind.UserDefinedOperator || methodSymbol.MethodKind == MethodKind.Conversion;
        }

        public static bool IsOperator(this Symbol symbol)
        {
            return symbol.Kind == SymbolKind.Method && IsOperator((MethodSymbol)symbol);
        }

        public static bool IsIndexer(this Symbol symbol)
        {
            return symbol.Kind == SymbolKind.Property && ((PropertySymbol)symbol).IsIndexer;
        }

        public static bool IsIndexedProperty(this Symbol symbol)
        {
            return symbol.Kind == SymbolKind.Property && ((PropertySymbol)symbol).IsIndexedProperty;
        }

        public static bool IsUserDefinedConversion(this Symbol symbol)
        {
            return symbol.Kind == SymbolKind.Method && ((MethodSymbol)symbol).MethodKind == MethodKind.Conversion;
        }

        /// <summary>
        /// Count the number of custom modifiers in/on the return type
        /// and parameters of the specified method.
        /// </summary>
        public static int CustomModifierCount(this MethodSymbol method)
        {
            int count = 0;

            count += method.ReturnTypeCustomModifiers.Length;
            count += method.ReturnType.CustomModifierCount();

            foreach (ParameterSymbol param in method.Parameters)
            {
                count += param.CustomModifiers.Length;
                count += param.Type.CustomModifierCount();
            }

            return count;
        }

        public static int CustomModifierCount(this Symbol m)
        {
            switch (m.Kind)
            {
                case SymbolKind.ArrayType:
                case SymbolKind.ErrorType:
                case SymbolKind.NamedType:
                case SymbolKind.PointerType:
                case SymbolKind.TypeParameter:
                    return ((TypeSymbol)m).CustomModifierCount();
                case SymbolKind.Event:
                    return ((EventSymbol)m).CustomModifierCount();
                case SymbolKind.Method:
                    return ((MethodSymbol)m).CustomModifierCount();
                case SymbolKind.Property:
                    return ((PropertySymbol)m).CustomModifierCount();
                default:
                    Debug.Assert(false);
                    return 0;
            }
        }

        public static int CustomModifierCount(this EventSymbol e)
        {
            return e.Type.CustomModifierCount();
        }

        /// <summary>
        /// Count the number of custom modifiers in/on the type
        /// and parameters (for indexers) of the specified property.
        /// </summary>
        public static int CustomModifierCount(this PropertySymbol property)
        {
            int count = 0;

            count += property.TypeCustomModifiers.Length;
            count += property.Type.CustomModifierCount();

            foreach (ParameterSymbol param in property.Parameters)
            {
                count += param.CustomModifiers.Length;
                count += param.Type.CustomModifierCount();
            }

            return count;
        }

        /// <summary>
        /// Count the number of custom modifiers in/on the return type
        /// and parameters of the specified method.
        /// </summary>
        public static bool HasCustomModifiers(this MethodSymbol method)
        {
            if (method.ReturnTypeCustomModifiers.Any() || method.ReturnType.HasCustomModifiers())
            {
                return true;
            }

            foreach (ParameterSymbol param in method.Parameters)
            {
                if (param.CustomModifiers.Any() || param.Type.HasCustomModifiers())
                {
                    return true;
                }
            }

            return false;
        }

        //UNDONE: HasCustomModifiers(PropertySymbol), HasCustomModifiers(FieldSymbol)

        internal static Symbol SymbolAsMember(this Symbol s, NamedTypeSymbol newOwner)
        {
            switch (s.Kind)
            {
                case SymbolKind.Field:
                    return ((FieldSymbol)s).AsMember(newOwner);
                case SymbolKind.Method:
                    return ((MethodSymbol)s).AsMember(newOwner);
                case SymbolKind.NamedType:
                    return ((NamedTypeSymbol)s).AsMember(newOwner);
                case SymbolKind.Property:
                    return ((PropertySymbol)s).AsMember(newOwner);
                case SymbolKind.Event:
                    return ((EventSymbol)s).AsMember(newOwner);
                default:
                    throw ExceptionUtilities.UnexpectedValue(s.Kind);
            }
        }

        /// <summary>
        /// Return the arity of a member.
        /// </summary>
        internal static int GetMemberArity(this Symbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)symbol).Arity;

                case SymbolKind.NamedType:
                case SymbolKind.ErrorType:
                    return ((NamedTypeSymbol)symbol).Arity;

                default:
                    return 0;
            }
        }

        internal static NamespaceOrTypeSymbol OfMinimalArity(this IEnumerable<NamespaceOrTypeSymbol> symbols)
        {
            NamespaceOrTypeSymbol minAritySymbol = null;
            int minArity = Int32.MaxValue;
            foreach (var symbol in symbols)
            {
                int arity = GetMemberArity(symbol);
                if (arity < minArity)
                {
                    minArity = arity;
                    minAritySymbol = symbol;
                }
            }

            return minAritySymbol;
        }

        internal static ImmutableArray<TypeParameterSymbol> GetMemberTypeParameters(this Symbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)symbol).TypeParameters;
                case SymbolKind.NamedType:
                case SymbolKind.ErrorType:
                    return ((NamedTypeSymbol)symbol).TypeParameters;
                case SymbolKind.Field:
                case SymbolKind.Property:
                case SymbolKind.Event:
                    return ImmutableArray<TypeParameterSymbol>.Empty;
                default:
                    Debug.Assert(false, String.Format("{0} is not a kind of member", symbol.Kind));
                    return ImmutableArray<TypeParameterSymbol>.Empty;
            }
        }

        internal static ImmutableArray<TypeSymbol> GetMemberTypeArgumentsNoUseSiteDiagnostics(this Symbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)symbol).TypeArguments;
                case SymbolKind.NamedType:
                case SymbolKind.ErrorType:
                    return ((NamedTypeSymbol)symbol).TypeArgumentsNoUseSiteDiagnostics;
                case SymbolKind.Field:
                case SymbolKind.Property:
                case SymbolKind.Event:
                    return ImmutableArray<TypeSymbol>.Empty;
                default:
                    Debug.Assert(false, String.Format("{0} is not a kind of member", symbol.Kind));
                    return ImmutableArray<TypeSymbol>.Empty;
            }
        }

        internal static bool IsParameterlessValueTypeConstructor(this MethodSymbol method, bool requireSynthesized = false)
        {
            return method.MethodKind == MethodKind.Constructor && method.ParameterCount == 0 &&
                method.ContainingType.IsValueType && (!requireSynthesized || method.IsImplicitlyDeclared);
        }

        /// <summary>
        /// If the event has a AddMethod, return that.  Otherwise check the overridden
        /// event, if any.  Repeat for each overridden event.
        /// </summary>
        /// <remarks>
        /// This method exists to mimic the behavior of GetOwnOrInheritedGetMethod, but it
        /// should only ever look at the overridden event in error scenarios.
        /// </remarks>
        internal static MethodSymbol GetOwnOrInheritedAddMethod(this EventSymbol @event)
        {
            while ((object)@event != null)
            {
                MethodSymbol addMethod = @event.AddMethod;
                if ((object)addMethod != null)
                {
                    return addMethod;
                }

                @event = @event.IsOverride ? @event.OverriddenEvent : null;
            }

            return null;
        }

        /// <summary>
        /// If the event has a RemoveMethod, return that.  Otherwise check the overridden
        /// event, if any.  Repeat for each overridden event.
        /// </summary>
        /// <remarks>
        /// This method exists to mimic the behavior of GetOwnOrInheritedSetMethod, but it
        /// should only ever look at the overridden event in error scenarios.
        /// </remarks>
        internal static MethodSymbol GetOwnOrInheritedRemoveMethod(this EventSymbol @event)
        {
            while ((object)@event != null)
            {
                MethodSymbol removeMethod = @event.RemoveMethod;
                if ((object)removeMethod != null)
                {
                    return removeMethod;
                }

                @event = @event.IsOverride ? @event.OverriddenEvent : null;
            }

            return null;
        }

        internal static bool IsExplicitInterfaceImplementation(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).IsExplicitInterfaceImplementation;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).IsExplicitInterfaceImplementation;
                case SymbolKind.Event:
                    return ((EventSymbol)member).IsExplicitInterfaceImplementation;
                default:
                    return false;
            }
        }

        internal static bool IsPartialMethod(this Symbol member)
        {
            var sms = member as SourceMethodSymbol;
            return (object)sms != null && sms.IsPartial;
        }

        internal static bool IsPartialImplementation(this Symbol member)
        {
            var sms = member as SourceMemberMethodSymbol;
            return (object)sms != null && sms.IsPartialImplementation;
        }

        internal static bool IsPartialDefinition(this Symbol member)
        {
            var sms = member as SourceMemberMethodSymbol;
            return (object)sms != null && sms.IsPartialDefinition;
        }

        internal static ImmutableArray<Symbol> GetExplicitInterfaceImplementations(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).ExplicitInterfaceImplementations.Cast<MethodSymbol, Symbol>();
                case SymbolKind.Property:
                    return ((PropertySymbol)member).ExplicitInterfaceImplementations.Cast<PropertySymbol, Symbol>();
                case SymbolKind.Event:
                    return ((EventSymbol)member).ExplicitInterfaceImplementations.Cast<EventSymbol, Symbol>();
                default:
                    return ImmutableArray<Symbol>.Empty;
            }
        }

        internal static TypeSymbol GetTypeOrReturnType(this Symbol member)
        {
            TypeSymbol returnType;
            ImmutableArray<CustomModifier> returnTypeCustomModifiers;
            GetTypeOrReturnType(member, out returnType, out returnTypeCustomModifiers);
            return returnType;
        }

        internal static void GetTypeOrReturnType(this Symbol member, out TypeSymbol returnType, out ImmutableArray<CustomModifier> returnTypeCustomModifiers)
        {
            switch (member.Kind)
            {
                case SymbolKind.Field:
                    FieldSymbol field = (FieldSymbol)member;
                    returnType = field.Type;
                    returnTypeCustomModifiers = ImmutableArray<CustomModifier>.Empty;
                    break;
                case SymbolKind.Method:
                    MethodSymbol method = (MethodSymbol)member;
                    returnType = method.ReturnType;
                    returnTypeCustomModifiers = method.ReturnTypeCustomModifiers;
                    break;
                case SymbolKind.Property:
                    PropertySymbol property = (PropertySymbol)member;
                    returnType = property.Type;
                    returnTypeCustomModifiers = property.TypeCustomModifiers;
                    break;
                case SymbolKind.Event:
                    EventSymbol @event = (EventSymbol)member;
                    returnType = @event.Type;
                    returnTypeCustomModifiers = ImmutableArray<CustomModifier>.Empty;
                    break;
                default:
                    Debug.Assert(false, "Unexpected member kind " + member.Kind);
                    returnType = null;
                    returnTypeCustomModifiers = ImmutableArray<CustomModifier>.Empty;
                    break;
            }
        }

        internal static Symbol GetOverriddenMember(this Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    return ((MethodSymbol)member).OverriddenMethod;
                case SymbolKind.Property:
                    return ((PropertySymbol)member).OverriddenProperty;
                case SymbolKind.Event:
                    return ((EventSymbol)member).OverriddenEvent;
                default:
                    throw ExceptionUtilities.UnexpectedValue(member.Kind);
            }
        }

        internal static Symbol GetLeastOverriddenMember(this Symbol member, NamedTypeSymbol accessingTypeOpt)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    var method = (MethodSymbol)member;
                    return method.GetConstructedLeastOverriddenMethod(accessingTypeOpt);

                case SymbolKind.Property:
                    var property = (PropertySymbol)member;
                    return property.GetLeastOverriddenProperty(accessingTypeOpt);

                case SymbolKind.Event:
                    var evnt = (EventSymbol)member;
                    return evnt.GetLeastOverriddenEvent(accessingTypeOpt);

                default:
                    return member;
            }
        }

        internal static bool IsFieldOrFieldLikeEvent(this Symbol member, out FieldSymbol field)
        {
            switch (member.Kind)
            {
                case SymbolKind.Field:
                    field = (FieldSymbol)member;
                    return true;
                case SymbolKind.Event:
                    field = ((EventSymbol)member).AssociatedField;
                    return (object)field != null;
                default:
                    field = null;
                    return false;
            }
        }

        internal static string GetMemberCallerName(this Symbol member)
        {
            if (member.Kind == SymbolKind.Method)
            {
                member = ((MethodSymbol)member).AssociatedPropertyOrEvent ?? member;
            }

            return member.IsIndexer() ? member.MetadataName :
                member.IsExplicitInterfaceImplementation() ? ExplicitInterfaceHelpers.GetMemberNameWithoutInterfaceName(member.Name) :
                member.Name;
        }
    }
}
