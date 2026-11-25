using Framework.Machine;
using YooAsset;

internal class FsmStartGame : IState<PatchContext>
{
    void IState<PatchContext>.OnEnter(PatchContext context)
    {
        PatchStepChangedEvent.SendEventMessage("资源准备完成");

        PatchManager.Instance.NotifyCompleted();
    }
    void IState<PatchContext>.OnExit(PatchContext context)
    {
    }
}