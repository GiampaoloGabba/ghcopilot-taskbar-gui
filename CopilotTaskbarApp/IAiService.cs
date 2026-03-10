using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotTaskbarApp;

public interface IAiService : IAsyncDisposable
{
    string ProviderName { get; }
    Task<string> GetResponseAsync(string prompt, string? context = null, string? imageBase64 = null, List<ChatMessage>? recentMessages = null, CancellationToken cancellationToken = default);
    Task<bool> CheckAuthenticationAsync();
}
