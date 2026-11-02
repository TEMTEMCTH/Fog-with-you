using UnityEngine;

/// <summary>
/// Метка предмета, который можно поднять. Вешается на префаб предмета
/// (вместе с NetworkObject и любым Collider).
/// itemId должен быть уникальным для каждого ВИДА предмета ("key", "spear", ...).
/// </summary>
public class PickupItem : MonoBehaviour
{
    public enum WeaponType
    {
        None,                // обычный предмет
        Melee,               // бьют в ближнем бою (палка)
        Throwable,           // бросают (камень)
        MeleeAndThrowable    // и бьют, и бросают (копьё)
    }

    [SerializeField] private string itemId = "item";
    [SerializeField] private string displayName = "Предмет";

    [Header("Оружие против монстра (необязательно)")]
    [Tooltip("Удар или бросок этим предметом оглушает монстра. Для копья — Melee And Throwable.")]
    [SerializeField] private WeaponType weapon = WeaponType.None;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public bool CanMelee => weapon == WeaponType.Melee || weapon == WeaponType.MeleeAndThrowable;
    public bool CanThrow => weapon == WeaponType.Throwable || weapon == WeaponType.MeleeAndThrowable;
}