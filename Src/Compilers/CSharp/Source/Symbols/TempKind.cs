﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    // Non-negative values will be used in generated names.
    // Must match corresponding Dev11 TEMP_KIND enum values for
    // compatibility with assemblies generated by the native compiler.
    // Negative values represent temporaries that should not be named.
    internal enum TempKind : short
    {
        None = -1, // TEMP_KIND uses 0 so None cannot be 0.
        Optimizer = -2,
        Lock = 2,
        Using = 3,
        ForEachEnumerator = 5,
        ForEachArray = 6,
        ForEachArrayIndex0 = 7,
        ForEachArrayLimit0 = ForEachArrayIndex0 + 256,
        FixedString = ForEachArrayLimit0 + 256,
        LockTaken = FixedString + 1, // No corresponding TEMP_KIND
    }
}
