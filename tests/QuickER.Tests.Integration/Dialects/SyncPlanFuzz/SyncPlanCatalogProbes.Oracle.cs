using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>Oracle の探針</summary>
    private static IReadOnlyDictionary<string, string> OracleProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
