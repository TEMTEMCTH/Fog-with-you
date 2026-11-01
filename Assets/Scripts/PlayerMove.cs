using Unity.Netcode;
using UnityEngine;

public class PlayerMove : NetworkBehaviour
{
    public float speed = 5f;
    public float mouseSensitivity = 1.5f; // Настрой в инспекторе
    private float xRotation = 0f;
    private Camera myCam;
    private Vector3 lastMousePos;

    void Start()
    {
        if (IsOwner)
        {
            // Ищем камеру или создаем, если её нет
            myCam = GetComponentInChildren<Camera>();
            if (myCam == null)
            {
                GameObject camObj = new GameObject("PlayerCamera");
                camObj.transform.SetParent(transform);
                camObj.transform.localPosition = new Vector3(0, 0.5f, 0);
                myCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            // НЕ ПРЯЧЕМ КУРСОР, чтобы обойти баг с осью X
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Запоминаем начальную позицию мыши
            lastMousePos = Input.mousePosition;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // 1. СЧИТЫВАЕМ ПОЗИЦИЮ КУРСОРА НА ЭКРАНЕ
        Vector3 currentMousePos = Input.mousePosition;

        // 2. ВЫЧИСЛЯЕМ СМЕЩЕНИЕ (разницу между кадрами)
        float mouseX = (currentMousePos.x - lastMousePos.x) * mouseSensitivity * 0.1f;
        float mouseY = (currentMousePos.y - lastMousePos.y) * mouseSensitivity * 0.1f;

        // 3. КРУТИМ ТЕЛО ВЛЕВО-ВПРАВО
        transform.Rotate(Vector3.up * mouseX);

        // 4. КРУТИМ КАМЕРУ ВВЕРХ-ВНИЗ
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        if (myCam != null)
        {
            myCam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        // 5. ДВИЖЕНИЕ WASD
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        transform.Translate(move * speed * Time.deltaTime);

        // Обновляем последнюю позицию мыши для следующего кадра
        lastMousePos = currentMousePos;
    }
}