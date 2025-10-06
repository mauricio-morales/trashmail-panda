using System;
using System.Collections.Generic;
using TrashMailPanda.Shared.Models;
using TrashMailPanda.Providers.Contacts.Services;

namespace TrashMailPanda.Providers.Contacts.Models;

/// <summary>
/// Result of a contacts synchronization operation
/// </summary>
public class ContactsSyncResult
{
    /// <summary>
    /// Whether the sync operation was successful overall
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Total number of contacts synchronized across all adapters
    /// </summary>
    public int ContactsSynced { get; set; }

    /// <summary>
    /// Type of sync performed (Full, Incremental)
    /// </summary>
    public string SyncType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the sync was completed
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Individual results from each contact source adapter
    /// </summary>
    public List<AdapterSyncResult> AdapterResults { get; set; } = new();

    /// <summary>
    /// Any error messages from the sync operation
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Additional metadata about the sync operation
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}


