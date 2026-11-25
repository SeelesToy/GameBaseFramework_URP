using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

public class ProcedureMenu : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        Log.Info("Enter Procedure Menu");
        ChangeState<ProcedureBattle>(procedureOwner);
    }
}
