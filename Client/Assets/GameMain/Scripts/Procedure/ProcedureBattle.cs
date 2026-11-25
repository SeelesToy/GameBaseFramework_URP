using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

public class ProcedureBattle : ProcedureBase
{
    
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        Log.Info("Enter Procedure Battle HotUpdate Version101");
        var cha = ConfigSystem.Instance.Tables.TbHero.GetOrDefault(1);
        if (cha != null)
            Log.Info(cha);
    }
}
