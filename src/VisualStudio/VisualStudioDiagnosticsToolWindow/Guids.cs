// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

// MUST match guids.h

using System;

namespace Roslyn.VisualStudio.DiagnosticsWindow
{
    internal static class GuidList
    {
        public const string guidVisualStudioDiagnosticsWindowPkgString = "49e24138-9ee3-49e0-8ede-6b39f49303bf";
        public const string guidVisualStudioDiagnosticsWindowCmdSetString = "f22c2499-790a-4b6c-b0fd-b6f0491e1c9c";
        public const string guidToolWindowPersistanceString = "b2da68d7-fd1c-491a-a9a0-24f597b9f56c";

        public static readonly Guid guidVisualStudioDiagnosticsWindowCmdSet = new Guid(guidVisualStudioDiagnosticsWindowCmdSetString);
    };
}
