﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Specifies how to display delegates (just the name or the name with the signature).
    /// </summary>
    public enum SymbolDisplayDelegateStyle
    {
        /// <summary>
        /// Shows only the name of the delegate (e.g. "SomeDelegate").
        /// </summary>
        NameOnly,

        /// <summary>
        /// Shows the name and the parameters of the delegate (e.g. "SomeDelegate(int x)").  
        /// </summary>
        /// <remarks>
        /// The format of the parameters will be determined by the other flags passed.
        /// </remarks>
        NameAndParameters,

        /// <summary>
        /// Shows the name and the signature of the delegate (e.g. "void SomeDelegate(int x)").  
        /// </summary>
        /// <remarks>
        /// The format of the signature will be determined by the other flags passed.
        /// </remarks>
        NameAndSignature,
    }
}