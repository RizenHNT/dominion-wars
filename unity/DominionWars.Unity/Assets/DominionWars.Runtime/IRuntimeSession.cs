using DominionWars.Adapters;

namespace DominionWars.Unity.Runtime
{

public interface IRuntimeSession
{
    RuntimeSnapshotEnvelope GetSnapshot(int viewerPlayerIndex);
    RuntimeActionSubmission Submit(RuntimeGameAction action);
    void Close();
}
}
