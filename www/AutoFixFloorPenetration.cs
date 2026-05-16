using UnityEngine;

/**
 * AutoFixFloorPenetration — при входе в рагдол выравнивает корневую кость
 * по нормали поверхности под ней.
 *
 * ЗАЧЕМ: При переходе из анимации в рагдол персонаж может оказаться в
 * позе, где часть тела уже под полом (например, если анимация "сидит").
 * AutoFixFloorPenetration поднимает корневую кость и выравнивает тело
 * по поверхности, чтобы рагдол стартовал корректно.
 *
 * ПОЧЕМУ НЕ ДАСТ ПРОВАЛИТЬСЯ В ПОЛ:
 * - Работает в момент активации рагдола (один раз).
 * - Использует Physics.RaycastAll для поиска коллайдеров пола.
 * - Выравнивает корневую кость по нормали (чтобы персонаж "лёг" на пол,
 *   а не воткнулся в него под углом).
 * - Дополнительно: поднимает все кости, которые ниже поверхности.
 */
public class AutoFixFloorPenetration : MonoBehaviour
{
    [Header("Настройки")]
    [Tooltip("Слой пола (Ground)")]
    public LayerMask groundLayer = 1 << 0;
    [Tooltip("Смещение над полом после выравнивания")]
    public float floorOffset = 0.1f;
    [Tooltip("Радиус сканирования под корнем")]
    public float scanRadius = 0.3f;
    [Tooltip("Максимальная дистанция луча вниз")]
    public float maxRayDistance = 2f;

    [Header("Кости")]
    [Tooltip("Корневая кость (Hips/Pelvis) — обычно самая нижняя")]
    public Transform rootBone;

    private Rigidbody[] allBones;

    void Awake()
    {
        allBones = GetComponentsInChildren<Rigidbody>();
    }

    /**
     * FixOnActivate — вызывается при входе в рагдол.
     */
    public void FixOnActivate()
    {
        if (rootBone == null)
        {
            // Пытаемся найти корневую кость по имени
            rootBone = FindBoneByName("hips") ?? FindBoneByName("pelvis") ?? FindBoneByName("root");
            if (rootBone == null)
            {
                Debug.LogWarning("[AutoFixFloorPenetration] Корневая кость не найдена! " +
                    "Задайте вручную в инспекторе.");
                return;
            }
        }

        // 1) Определяем высоту пола под корнем
        float floorY = GetFloorBelow(rootBone.position);

        if (rootBone.position.y < floorY + floorOffset)
        {
            // 2) Поднимаем корень
            Vector3 newPos = rootBone.position;
            newPos.y = floorY + floorOffset;
            rootBone.position = newPos;

            // 3) Выравниваем корень по нормали поверхности
            AlignToSurfaceNormal(rootBone, floorY);
        }

        // 4) Проверяем и поднимаем все остальные кости
        if (allBones != null)
        {
            foreach (var rb in allBones)
            {
                if (rb == null || rb.isKinematic) continue;

                float boneFloorY = GetFloorBelow(rb.position);
                if (rb.position.y < boneFloorY + floorOffset)
                {
                    Vector3 bonePos = rb.position;
                    bonePos.y = boneFloorY + floorOffset;
                    rb.position = bonePos;
                }
            }
        }
    }

    private float GetFloorBelow(Vector3 position)
    {
        RaycastHit hit;

        // Луч вниз от позиции
        if (Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, out hit, maxRayDistance, groundLayer))
        {
            return hit.point.y;
        }

        // Если луча нет — пробуем Terrain
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            return terrain.SampleHeight(position) + terrain.transform.position.y;
        }

        // По умолчанию 0
        return 0f;
    }

    private void AlignToSurfaceNormal(Transform boneTransform, float floorY)
    {
        // Проверяем нормаль поверхности под корнем
        RaycastHit hit;
        Vector3 origin = boneTransform.position + Vector3.up * 0.1f;

        if (Physics.Raycast(origin, Vector3.down, out hit, maxRayDistance, groundLayer))
        {
            Vector3 surfaceNormal = hit.normal;

            // Выравниваем корень: up-вектор кости = нормаль поверхности
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);

            // Сохраняем вращение вокруг оси Y (направление лица)
            float originalYaw = boneTransform.eulerAngles.y;
            Vector3 targetEuler = targetRotation.eulerAngles;
            targetEuler.y = originalYaw;

            boneTransform.rotation = Quaternion.Euler(targetEuler);
        }
    }

    private Transform FindBoneByName(string boneName)
    {
        string lower = boneName.ToLower();

        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child.name.ToLower().Contains(lower))
            {
                return child;
            }
        }

        return null;
    }
}
