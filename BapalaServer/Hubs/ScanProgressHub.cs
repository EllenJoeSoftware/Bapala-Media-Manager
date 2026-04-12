using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BapalaServer.Hubs;

[Authorize]
public class ScanProgressHub : Hub
{
    // Clients connect here to receive scan progress events pushed by MediaController.
    // Events pushed via IHubContext<ScanProgressHub>:
    //   "ScanStarted"   { folders }
    //   "ScanProgress"  { currentFile, processed, total }
    //   "ScanCompleted" { added, updated, skipped, errors }
    //   "ScanError"     { error }
}
