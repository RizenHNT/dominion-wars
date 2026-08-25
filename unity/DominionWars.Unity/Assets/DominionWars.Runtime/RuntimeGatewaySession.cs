using System;
using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

/// <summary>Unity transport session backed by the in-process C# gateway.</summary>
public sealed class RuntimeGatewaySession : IRuntimeSession
{
    private readonly RuntimeMatchGateway _gateway;
    private bool _closed;

    public RuntimeGatewaySession(RuntimeMatchGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex)
    {
        EnsureOpen();
        return _gateway.GetSnapshot(viewerPlayerIndex);
    }

    public RuntimeActionSubmission Submit(RuntimeGameAction action)
    {
        EnsureOpen();
        return _gateway.Submit(action);
    }

    public void Close()
    {
        _closed = true;
    }

    private void EnsureOpen()
    {
        if (_closed) throw new ObjectDisposedException(nameof(RuntimeGatewaySession));
    }
}
}
