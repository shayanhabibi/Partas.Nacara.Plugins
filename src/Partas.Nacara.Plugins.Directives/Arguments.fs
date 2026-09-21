namespace Nacara.Plugins

/// <summary>Reads arguments written on directive's opening line.</summary>
module internal Arguments =
    /// <summary>Placeholder, replaced in next task.</summary>
    let toFlowMapping (_arguments: string) : Result<string, string> = Ok "{}"
