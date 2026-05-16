using UnityEngine;

/**
 * RagdollStabilizer — защита от проваливания костей сквозь пол.
 *
 * ПРИНЦИП: Каждый коллайдер в рагдоле проверяется на penetration.
 * Если кость ушла ниже поверхности (Y < sampleHeight + threshold),
 * мы принудительно поднимаем её Rigidbody.position.
 *
 * Почему это не даст провалиться в пол:
 * - Все коллайдеры рагдола работают в слое "Ragdoll".
 * - Слой "Ragdoll" НЕ коллайдит со слоем "GroundCollision" (отключаем
 *   стандартное взаимодействие, чтобы физика не заталкивала кости вниз).
 * - RagdollStabilizer работает в LateUpdate — после физики, но до рендера.
 * - Дополнительно: Raycast вниз от таза, чтобы точно определить высоту пола.
 */
public class RagdollStabilizer : MonoBehaviour
{
    [Header("Настройки стабилизации")]
    [Tooltip("Допуск: если кость ниже пола + этот допуск — поднимаем")]
    public float floorThreshold = 0.15f;
    [Tooltip("Максимальная скорость подъёма кости за кадр")]
    public float maxLiftSpeed = 2.0f;
    [Tooltip("Высота, на которую поднимаем над полом")]
    public float liftOffset = 0.2f;

    [Header("Слои (задаются в инспекторе)")]
    public LayerMask groundLayer = 1 << 0; // Default

    private Rigidbody[] bones;
    private Collider[] boneColliders;
    private bool isActive = false;

    void Awake()
    {
        // Собираем все Rigidbody в рагдоле (дочерние компоненты)
        bones = GetComponentsInChildren<Rigidbody>();
        boneColliders = GetComponentsInChildren<Collider>();

        // Устанавливаем слои: все кости рагдола — на "Ragdoll"
        int ragdollLayer = LayerMask.NameToLayer("Ragdoll");
        if (ragdollLayer == -1)
        {
            Debug.LogError("[RagdollStabilizer] Слой 'Ragdoll' не найден! " +
                "Создайте слой 'Ragdoll' в Project Settings -> Tags and Layers");
            return;
        }

        foreach (var col in boneColliders)
        {
            col.gameObject.layer = ragdollLayer;
        }

        // Отключаем коллизию между слоями Ragdoll и GroundCollision
        int groundCollisionLayer = LayerMask.NameToLayer("GroundCollision");
        if (groundCollisionLayer != -1)
        {
            Physics.IgnoreLayerCollision(ragdollLayer, groundCollisionLayer, true);
        }
    }

    public void Activate()
    {
        isActive = true;
    }

    public void Deactivate()
    {
        isActive = false;
    }

    void LateUpdate()
    {
        if (!isActive || bones == null) return;

        foreach (var rb in bones)
        {
            // Пропускаем кинематические кости
            if (rb.isKinematic) continue;

            Vector3 pos = rb.position;

            // Определяем высоту пола под костью через Raycast
            float floorY = GetFloorHeight(pos);

            if (pos.y < floorY + floorThreshold)
            {
                // Поднимаем кость над полом
                float targetY = floorY + liftOffset;
                float newY = Mathf.MoveTowards(pos.y, targetY, maxLiftSpeed * Time.deltaTime);

                rb.position = new Vector3(pos.x, newY, pos.z);

                // Гасим вертикальную скорость, чтобы не провалился снова
                if (rb.velocity.y < 0)
                {
                    Vector3 vel = rb.velocity;
                    vel.y = Mathf.Min(vel.y, 0);
                    rb.velocity = vel;
                }
            }
        }
    }

    private float GetFloorHeight(Vector3 position)
    {
        RaycastHit hit;
        float rayLength = 1.0f;

        // Кидаем луч вниз от позиции кости
        if (Physics.Raycast(position, Vector3.down, out hit, rayLength, groundLayer))
        {
            return hit.point.y;
        }

        // Если луча нет — пробуем Terrain
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float terrainY = terrain.SampleHeight(position) + terrain.transform.position.y;
            return terrainY;
        }

        // По умолчанию пол на Y = 0
        return 0f;
    }

    // Визуализация Raycast в редакторе
    void OnDrawGizmosSelected()
    {
        if (bones == null) return;
        Gizmos.color = Color.green;
        foreach (var rb in bones)
        {
            Vector3 floorPos = new Vector3(rb.position.x, GetFloorHeight(rb.position), rb.position.z);
            Gizmos.DrawLine(rb.position, floorPos);
            Gizmos.DrawWireSphere(floorPos, 0.1f);
        }
    }
}
