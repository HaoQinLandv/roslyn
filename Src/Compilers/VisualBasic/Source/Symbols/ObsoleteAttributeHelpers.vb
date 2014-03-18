﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Diagnostics
Imports System.Threading
Imports System.Reflection.Metadata
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols

    Friend Class ObsoleteAttributeHelpers

        ''' <summary>
        ''' Initialize the ObsoleteAttributeData by fetching attributes and decoding ObsoleteAttributeData. This can be 
        ''' done for Metadata symbol easily whereas trying to do this for source symbols could result in cycles.
        ''' </summary>
        Friend Shared Sub InitializeObsoleteDataFromMetadata(ByRef data As ObsoleteAttributeData, token As Handle, containingModule As PEModuleSymbol)
            If data Is ObsoleteAttributeData.Uninitialized Then
                Dim obsoleteAttributeData As ObsoleteAttributeData = Nothing
                Dim isObsolete As Boolean = containingModule.Module.HasDeprecatedOrObsoleteAttribute(token, obsoleteAttributeData)
                Debug.Assert(isObsolete = (obsoleteAttributeData IsNot Nothing))
                Debug.Assert(obsoleteAttributeData Is Nothing OrElse Not obsoleteAttributeData.IsUninitialized)
                Interlocked.CompareExchange(data, obsoleteAttributeData, obsoleteAttributeData.Uninitialized)
            End If
        End Sub

        ''' <summary>
        ''' This method checks to see if the given symbol is Obsolete or if any symbol in the parent hierarchy is Obsolete.
        ''' </summary>
        ''' <returns>
        ''' True if some symbol in the parent hierarchy is known to be Obsolete. Unknown if any
        ''' symbol's Obsoleteness is Unknown. False, if we are certain that no symbol in the parent
        ''' hierarchy is Obsolete.
        ''' </returns>
        Friend Shared Function GetObsoleteContextState(symbol As Symbol, Optional forceComplete As Boolean = False) As ThreeState
            If symbol Is Nothing Then
                Return ThreeState.False
            End If

            If forceComplete Then
                symbol.ForceCompleteObsoleteAttribute()
            End If

            Dim associatedPropertyOrEvent = If(symbol.IsAccessor(), DirectCast(symbol, MethodSymbol).AssociatedPropertyOrEvent, Nothing)

            ' If this is an event accessor, then consider the event itself for context since
            ' event accessors cannot be marked obsolete.
            If associatedPropertyOrEvent IsNot Nothing AndAlso associatedPropertyOrEvent.Kind = SymbolKind.Event Then
                symbol = DirectCast(symbol, MethodSymbol).AssociatedPropertyOrEvent
            End If

            If symbol.ObsoleteState <> ThreeState.False Then
                Return symbol.ObsoleteState
            End If

            ' If this is a property accessor then check the property for obsoleteness as well so that we suppress
            ' obsolete diagnostics anywhere inside a property.
            If associatedPropertyOrEvent IsNot Nothing AndAlso associatedPropertyOrEvent.Kind = SymbolKind.Property Then
                Return GetObsoleteContextState(associatedPropertyOrEvent, forceComplete)
            Else
                Return GetObsoleteContextState(symbol.ContainingSymbol, forceComplete)
            End If
        End Function


        ''' <summary>
        ''' Create a diagnostic for the given symbol. This could be an error or a warning based on
        ''' the ObsoleteAttribute's arguments.
        ''' </summary>
        Friend Shared Function CreateObsoleteDiagnostic(symbol As Symbol) As DiagnosticInfo
            Dim data = symbol.ObsoleteAttributeData

            If (data Is Nothing) Then
                ' ObsoleteAttribute had errors.
                Return Nothing
            End If

            ' At this point, we are going to issue diagnostics and therefore the data shouldn't be
            ' uninitialized.
            Debug.Assert(Not data.IsUninitialized)

            ' For property accessors we report a special diagnostic which indicates whether the getter or setter is obsolete.
            ' For all other symbols, report the regular diagnostic.
            If symbol.IsAccessor() AndAlso (DirectCast(symbol, MethodSymbol).AssociatedPropertyOrEvent).Kind = SymbolKind.Property Then
                Dim accessorSymbol = DirectCast(symbol, MethodSymbol)
                Dim accesorString = If(accessorSymbol.MethodKind = MethodKind.PropertyGet, "Get", "Set")

                If String.IsNullOrEmpty(data.Message) Then
                    Return ErrorFactory.ErrorInfo(If(data.IsError, ERRID.ERR_UseOfObsoletePropertyAccessor2, ERRID.WRN_UseOfObsoletePropertyAccessor2),
                                        accesorString, accessorSymbol.AssociatedPropertyOrEvent)
                Else
                    Return ErrorFactory.ErrorInfo(If(data.IsError, ERRID.ERR_UseOfObsoletePropertyAccessor3, ERRID.WRN_UseOfObsoletePropertyAccessor3),
                                        accesorString, accessorSymbol.AssociatedPropertyOrEvent, data.Message)
                End If
            Else
                If String.IsNullOrEmpty(data.Message) Then
                    Return ErrorFactory.ErrorInfo(If(data.IsError, ERRID.ERR_UseOfObsoleteSymbolNoMessage1, ERRID.WRN_UseOfObsoleteSymbolNoMessage1), symbol)
                Else
                    Return ErrorFactory.ErrorInfo(If(data.IsError, ERRID.ERR_UseOfObsoleteSymbol2, ERRID.WRN_UseOfObsoleteSymbol2), symbol, data.Message)
                End If
            End If

        End Function

    End Class
End Namespace
