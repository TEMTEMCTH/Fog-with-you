using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Инвентарь игрока на 4 слота (всегда виден на экране).
///   1–4 — взять предмет из слота в руку
///   E   — поднять предмет, на который смотрите
///   Q   — выбросить предмет, который в руке
/// Содержимое хранит СЕРВЕР (NetworkList), клиент только просит что-то сделать —
/// поэтому предмет нельзя подобрать дважды, а мир у всех игроков одинаковый.
/// Предмет в руке виден всем игрокам. Вешается на префаб игрока.
/// </summary>
public class PlayerInventory : NetworkBehaviour
{
    private const int SlotCount = 4;

    [Header("Предметы")]
    [Tooltip("Префабы ВСЕХ предметов, которые можно взять (с PickupItem и NetworkObject).")]
    [SerializeField] private PickupItem[] itemPrefabs;

    [Header("Взаимодействие")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private float dropDistance = 1.3f;

    [Header("Предмет в руке")]
    [SerializeField] private Vector3 firstPersonHandOffset = new Vector3(0.35f, -0.3f, 0.7f); // относительно камеры
    [SerializeField] private Vector3 thirdPersonHandOffset = new Vector3(0.5f, 0.1f, 0.5f);   // относительно тела (для других игроков)
    [SerializeField] private float handScale = 0.6f;

    // 4 слота; пустой слот = пустая строка. Читает владелец (и сервер), пишет сервер.
    private NetworkList<FixedString32Bytes> items;
    // Что сейчас в руке — видят ВСЕ игроки
    private NetworkVariable<FixedString32Bytes> heldItemId;

    private Camera cam;
    private InventoryUI ui;
    private PickupItem lookedAt;
    private GameObject heldRoot;
    private int selected;        // выбранный слот (на клиенте владельца)
    private int serverSelected;  // выбранный слот (на сервере)

    private void Awake()
    {
        items = new NetworkList<FixedString32Bytes>(
            new System.Collections.Generic.List<FixedString32Bytes>(),
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        heldItemId = new NetworkVariable<FixedString32Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && items.Count == 0)
            for (int i = 0; i < SlotCount; i++)
                items.Add(default);

        cam = GetComponentInChildren<Camera>(true);

        heldItemId.OnValueChanged += OnHeldChanged;
        RebuildHeldVisual(heldItemId.Value.ToString());

        if (!IsOwner)
        {
            enabled = false;   // у чужих игроков ввод и интерфейс не нужны (RPC на сервере всё равно работают)
            return;
        }

        ui = InventoryUI.Create(SlotCount);
    }

    public override void OnNetworkDespawn()
    {
        heldItemId.OnValueChanged -= OnHeldChanged;
        if (heldRoot != null) Destroy(heldRoot);
        if (ui != null) Destroy(ui.gameObject);
    }

    // ───────────── Клиент (владелец) ─────────────

    private void Update()
    {
        if (ui == null) return;

        bool blocked = PauseMenu.IsOpen || Cursor.lockState != CursorLockMode.Locked;

        lookedAt = blocked ? null : FindLookedItem();

        if (!blocked)
        {
            int number = NumberPressed();
            if (number >= 0 && number != selected)
            {
                selected = number;
                SelectSlotServerRpc(selected);
            }

            if (InteractPressed() && lookedAt != null) TryPickup();
            if (DropPressed()) TryDrop();
        }

        RefreshUI();
    }

    private PickupItem FindLookedItem()
    {
        if (cam == null) return null;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<PickupItem>();
        return null;
    }

    private void TryPickup()
    {
        if (!HasEmptySlot())
        {
            ui.ShowMessage("Инвентарь полон");
            return;
        }

        var netObj = lookedAt.GetComponentInParent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning("У предмета нет компонента NetworkObject!", lookedAt);
            return;
        }
        PickupServerRpc(netObj);
    }

    private void TryDrop()
    {
        if (selected >= items.Count || items[selected].IsEmpty)
        {
            ui.ShowMessage("В руке ничего нет");
            return;
        }
        DropServerRpc(selected);
    }

    private void RefreshUI()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            string label = (i < items.Count && !items[i].IsEmpty) ? GetDisplayName(items[i].ToString()) : "";
            ui.SetSlot(i, label, i == selected);
        }

        if (lookedAt != null)
        {
            string suffix = HasEmptySlot() ? "" : "  (инвентарь полон)";
            ui.SetPrompt("[E] Поднять: " + lookedAt.DisplayName + suffix);
        }
        else
        {
            ui.SetPrompt("");
        }
    }

    private bool HasEmptySlot()
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].IsEmpty) return true;
        return false;
    }

    private string GetDisplayName(string itemId)
    {
        var prefab = FindPrefab(itemId);
        return prefab != null ? prefab.DisplayName : itemId;
    }

    private PickupItem FindPrefab(string itemId)
    {
        if (itemPrefabs == null) return null;
        foreach (var p in itemPrefabs)
            if (p != null && p.ItemId == itemId) return p;
        return null;
    }

    // ───────────── Предмет в руке (видят все) ─────────────

    private void OnHeldChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue)
    {
        RebuildHeldVisual(newValue.ToString());
    }

    /// <summary>
    /// Строит «копию» предмета для руки из мешей префаба — без NetworkObject и коллайдеров,
    /// поэтому дополнительно ничего настраивать не нужно.
    /// </summary>
    private void RebuildHeldVisual(string itemId)
    {
        if (heldRoot != null) Destroy(heldRoot);
        heldRoot = null;

        if (string.IsNullOrEmpty(itemId)) return;
        var prefab = FindPrefab(itemId);
        if (prefab == null) return;

        bool firstPerson = IsOwner && cam != null;

        heldRoot = new GameObject("HeldItem");
        heldRoot.transform.SetParent(firstPerson ? cam.transform : transform, false);
        heldRoot.transform.localPosition = firstPerson ? firstPersonHandOffset : thirdPersonHandOffset;
        heldRoot.transform.localRotation = Quaternion.identity;

        foreach (var meshFilter in prefab.GetComponentsInChildren<MeshFilter>())
        {
            var meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshRenderer == null || meshFilter.sharedMesh == null) continue;

            Matrix4x4 m = prefab.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;

            var part = new GameObject("Part");
            part.transform.SetParent(heldRoot.transform, false);
            part.transform.localPosition = m.GetPosition() * handScale;
            part.transform.localRotation = m.rotation;
            part.transform.localScale = m.lossyScale * handScale;
            part.AddComponent<MeshFilter>().sharedMesh = meshFilter.sharedMesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = meshRenderer.sharedMaterials;
        }
    }

    // ───────────── Сервер ─────────────

    [ServerRpc]
    private void SelectSlotServerRpc(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        serverSelected = index;
        RefreshHeld();
    }

    [ServerRpc]
    private void PickupServerRpc(NetworkObjectReference itemRef)
    {
        // Предмет мог уже забрать другой игрок — тогда ссылка не сработает
        if (!itemRef.TryGet(out NetworkObject netObj)) return;

        var item = netObj.GetComponentInChildren<PickupItem>();
        if (item == null) return;

        // Проверка расстояния (защита от подбора через всю карту)
        if (Vector3.Distance(transform.position, netObj.transform.position) > interactRange + 3f) return;

        int slot = FindTargetSlot();
        if (slot < 0) return;   // нет места

        items[slot] = new FixedString32Bytes(item.ItemId);
        netObj.Despawn(true);
        RefreshHeld();
    }

    [ServerRpc]
    private void DropServerRpc(int index)
    {
        if (index < 0 || index >= items.Count) return;
        if (items[index].IsEmpty) return;

        string itemId = items[index].ToString();
        var prefab = FindPrefab(itemId);
        if (prefab == null)
        {
            Debug.LogError($"PlayerInventory: префаб предмета «{itemId}» не добавлен в список Item Prefabs!", this);
            return;
        }

        // Точка перед игроком, затем опускаем предмет на землю
        Vector3 dropPoint = transform.position + transform.forward * dropDistance;
        Vector3 rayOrigin = dropPoint + Vector3.up * 1.5f;
        bool hitGround = Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, ~0,
            QueryTriggerInteraction.Ignore);

        var instance = Instantiate(prefab, hitGround ? hit.point : dropPoint, Quaternion.identity);

        // Поднимаем так, чтобы предмет стоял НА поверхности, а не торчал в ней
        var col = instance.GetComponentInChildren<Collider>();
        if (hitGround && col != null)
        {
            Physics.SyncTransforms();
            instance.transform.position += Vector3.up * (hit.point.y - col.bounds.min.y);
        }

        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"У префаба «{itemId}» нет компонента NetworkObject!", prefab);
            Destroy(instance.gameObject);
            return;
        }

        netObj.Spawn();
        items[index] = default;
        RefreshHeld();
    }

    // Новый предмет кладём в выбранный слот, если он пуст, иначе — в первый свободный
    private int FindTargetSlot()
    {
        if (serverSelected < items.Count && items[serverSelected].IsEmpty) return serverSelected;
        for (int i = 0; i < items.Count; i++)
            if (items[i].IsEmpty) return i;
        return -1;
    }

    private void RefreshHeld()
    {
        heldItemId.Value = serverSelected < items.Count ? items[serverSelected] : default;
    }

    // ───────────── Ввод (поддержка обеих систем) ─────────────
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool InteractPressed() => Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    private bool DropPressed()     => Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;

    private int NumberPressed()
    {
        var k = Keyboard.current;
        if (k == null) return -1;
        for (int i = 0; i < SlotCount; i++)
            if (k[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) return i;
        return -1;
    }
#else
    private bool InteractPressed() => Input.GetKeyDown(KeyCode.E);
    private bool DropPressed()     => Input.GetKeyDown(KeyCode.Q);

    private int NumberPressed()
    {
        for (int i = 0; i < SlotCount; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
        return -1;
    }
#endif
}
