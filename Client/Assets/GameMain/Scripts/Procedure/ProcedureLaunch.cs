using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

public class ProcedureLaunch : ProcedureBase
{
    private float switchTimer = 3f;
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);

        var baseComponent = GameEntry.GetComponent<BaseComponent>();
        if (baseComponent == null)
        {
            throw new System.InvalidOperationException("GameModule requires the Game Framework Base component.");
        }

        if (baseComponent.GetComponent<GameModule>() == null)
        {
            baseComponent.gameObject.AddComponent<GameModule>();
        }

        GameModule.InitFrameWorkComponents();
        GameModule.InitCustomComponents();
        Log.Info("游戏启动");
        switchTimer = 3f;
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
        switchTimer -= elapseSeconds;
        if (switchTimer <= 0f)
        {
            ChangeState<ProcedureMenu>(procedureOwner);
            return;
        }
    }
}
