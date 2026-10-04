/// <summary>可選敵人擊退資格；掛在 Enemy Root 的啟用元件可依 Boss 階段等正式狀態拒絕擊退。</summary>
public interface IEnemyAbilityKnockbackCondition
{
    bool CanBeKnockedBack(EnemyStateController enemy);
}
