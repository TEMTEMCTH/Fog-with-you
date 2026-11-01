using UnityEngine;

/// <summary>
/// Метка предмета, который можно поднять. Вешается на префаб предмета
/// (вместе с NetworkObject и любым Collider).
/// itemId должен быть уникальным для каждого ВИДА предмета ("key", "lamp", ...).
/// </summary>
public class PickupItem : MonoBehaviour
{
    [SerializeField] private string itemId = "item";
    [SerializeField] private string displayName = "Предмет";

    public string ItemId => itemId;
    public string DisplayName => displayName;
}
