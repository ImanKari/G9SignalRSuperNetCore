; Unshipped analyzer release.
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|--------------------------------------------------------------
G9001   | G9SignalRSuperNetCore  | Warning  | Hub method has unsupported return type.
G9002   | G9SignalRSuperNetCore  | Error    | Listener method must return Task or ValueTask.
G9003   | G9SignalRSuperNetCore  | Warning  | Hub method has too many parameters.
G9004   | G9SignalRSuperNetCore  | Warning  | Generic hub method is not supported.
G9005   | G9SignalRSuperNetCore  | Info     | Hub does not declare a route pattern.
G9006   | G9SignalRSuperNetCore  | Warning  | Could not locate the listener interface.
G9007   | G9SignalRSuperNetCore  | Error    | Internal generator error.
