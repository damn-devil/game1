using UnityEngine;

/**
 * MountController — управление посадкой на лошадь и отделением при ударе.
 *
 * ПРИНЦИП РАБОТЫ:
 * - При посадке персонаж прикрепляется к точке седла через "виртуальный"
 *   SoftJoint (пружинистый джойнт). Мы не используем настоящий Joint,
 *   а обновляем позицию персонажа относительно лошади с Spring-интерполяцией.
 * - При столкновении на скорости > threshold джойнт РАЗРЫВАЕТСЯ,
 *   и персонаж переходит в активный рагдол.
 * - Импульс при падении = скорость лошади * направление + вверх (красивое
 *   отделение от лошади, как в RDR2).
 *
 * КАК РАБОТАЕТ ОТДЕЛЕНИЕ ОТ ЛОШАДИ:
 * 1) MountController.CharacterController непрерывно следит за скоростью.
 * 2) Если скорость > threshold (4 м/с) И столкновение — вызывается Dismount().
 * 3) Dismount() отключает привязку к седлу, включает ActiveRagdoll,
 *    добавляет импульс: character.velocity = horseVelocity + Vector3.up * 2.
 * 4) Через 2 секунды проверяем дистанцию до лошади — если < 2 метров,
 *    показываем кнопку "Сесть" (E).
 * 5) Задержка перед возвратом в анимацию: 3 секунды (подъём).
 */
public class MountController : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Персонаж (GameObject с CharacterController или Rigidbody)")]
    public GameObject character;
    [Tooltip("Лошадь (Rigidbody)")]
    public Rigidbody horseRigidbody;
    [Tooltip("Точка седла (Transform, куда садится персонаж)")]
    public Transform saddlePoint;

    [Header("Физика прикрепления (Spring)")]
    [Tooltip("Жёсткость пружины (притяжение к седлу)")]
    public float springStiffness = 500f;
    [Tooltip("Демпфирование (чтобы не болтало)")]
    public float springDamper = 50f;

    [Header("Параметры отделения")]
    [Tooltip("Скорость, при которой персонаж слетает с лошади (м/с)")]
    public float dismountSpeedThreshold = 3f;
    [Tooltip("Минимальный импульс для отделения (от машин/взрывов)")]
    public float minImpulseForDismount = 500f;
    [Tooltip("Сила импульса вверх при падении")]
    public float dismountUpForce = 2f;
    [Tooltip("Множитель импульса в сторону движения")]
    public float dismountForwardForce = 1.2f;

    [Header("Тайминги")]
    [Tooltip("Задержка перед возвратом в анимацию (сек)")]
    public float ragdollDuration = 3f;
    [Tooltip("Задержка перед появлением кнопки 'Сесть' (сек)")]
    public float remountCheckDelay = 2f;

    [Header("Ссылки на скрипты")]
    public ActiveRagdoll activeRagdoll;
    public RagdollStabilizer stabilizer;

    // Состояния
    public bool IsMounted { get; private set; }
    public bool IsRagdoll { get; private set; }

    private Rigidbody characterRigidbody;
    private float currentSpeed;
    private float ragdollTimer = 0f;
    private float remountTimer = 0f;
    private Vector3 lastHorsePosition;
    private bool canRemount = false;

    // Событие для UI
    public System.Action<bool> OnMountStateChanged;
    public System.Action<bool> OnRemountAvailable;

    void Start()
    {
        if (character != null)
        {
            characterRigidbody = character.GetComponent<Rigidbody>();
            if (characterRigidbody == null)
            {
                Debug.LogError("[MountController] У персонажа нет Rigidbody! " +
                    "Добавьте Rigidbody для работы физики отделения.");
            }
        }

        lastHorsePosition = horseRigidbody != null ? horseRigidbody.position : Vector3.zero;

        if (activeRagdoll == null)
            activeRagdoll = GetComponent<ActiveRagdoll>();
        if (stabilizer == null)
            stabilizer = GetComponent<RagdollStabilizer>();
    }

    void FixedUpdate()
    {
        if (horseRigidbody == null) return;

        // Считаем скорость лошади
        Vector3 horseVelocity = (horseRigidbody.position - lastHorsePosition) / Time.fixedDeltaTime;
        currentSpeed = horseVelocity.magnitude;
        lastHorsePosition = horseRigidbody.position;

        if (IsMounted && !IsRagdoll)
        {
            // Spring-интерполяция к точке седла
            SpringToSaddle(horseVelocity);

            // Проверка: не превышена ли скорость
            if (currentSpeed > dismountSpeedThreshold)
            {
                // Проверяем столкновение через OnCollisionEnter (обрабатывается ниже)
                // Если есть коллизия — Dismount
            }
        }

        if (IsRagdoll)
        {
            ragdollTimer -= Time.fixedDeltaTime;

            if (ragdollTimer < ragdollDuration - remountCheckDelay && !canRemount)
            {
                // Проверяем дистанцию до лошади
                if (Vector3.Distance(character.transform.position, horseRigidbody.position) < 2f)
                {
                    canRemount = true;
                    OnRemountAvailable?.Invoke(true);
                }
            }

            if (ragdollTimer <= 0f)
            {
                // Возврат в анимацию
                RecoverFromRagdoll();
            }
        }
    }

    private void SpringToSaddle(Vector3 horseVelocity)
    {
        if (character == null || saddlePoint == null) return;

        // Целевая позиция = точка седла
        Vector3 targetPos = saddlePoint.position;

        // Текущая позиция персонажа
        Vector3 currentPos = character.transform.position;

        // Сила пружины: F = -k * (pos - target) - d * velocity
        Vector3 displacement = currentPos - targetPos;
        Vector3 springForce = -springStiffness * displacement - springDamper * (characterRigidbody.velocity - horseVelocity);

        // Применяем силу к персонажу
        if (characterRigidbody != null && !characterRigidbody.isKinematic)
        {
            characterRigidbody.AddForce(springForce, ForceMode.Acceleration);
        }
        else
        {
            // Если нет Rigidbody — просто двигаем Transform
            character.transform.position = Vector3.Lerp(currentPos, targetPos, Time.fixedDeltaTime * 5f);
        }

        // Поворот персонажа = поворот лошади
        character.transform.rotation = Quaternion.Slerp(
            character.transform.rotation,
            horseRigidbody.rotation,
            Time.fixedDeltaTime * 10f);
    }

    /**
     * Mount — посадить персонажа на лошадь.
     */
    public void Mount()
    {
        if (IsMounted || IsRagdoll) return;

        IsMounted = true;
        canRemount = false;

        // Отключаем активный рагдол и стабилизатор
        if (activeRagdoll != null) activeRagdoll.Deactivate();
        if (stabilizer != null) stabilizer.Deactivate();

        // Переводим персонажа в кинематический режим (управление через Spring)
        if (characterRigidbody != null)
        {
            characterRigidbody.isKinematic = false;  // Не кинематик — работает физика
            characterRigidbody.useGravity = true;
        }

        OnMountStateChanged?.Invoke(true);
    }

    /**
     * Dismount — сбросить персонажа с лошади.
     *
     * КАК РАБОТАЕТ ОТДЕЛЕНИЕ ОТ ЛОШАДИ:
     * 1) При посадке персонаж прикреплён к седлу через Spring-силу.
     * 2) При ударе со скоростью > threshold:
     *    a) Отключаем привязку (IsMounted = false).
     *    b) Активируем ActiveRagdoll — тело напрягается.
     *    c) Активируем RagdollStabilizer — защита от провала.
     *    d) Добавляем импульс: скорость лошади * множитель + вверх.
     * 3) Импульс вверх (dismountUpForce = 2) гарантирует, что персонаж
     *    подлетит, отделится от лошади и упадёт рядом, а не под копыта.
     * 4) Приземление обрабатывается ActiveRagdoll (руки вперёд) +
     *    RagdollStabilizer (не даёт провалиться в пол).
     */
    public void Dismount(Vector3? customImpulse = null)
    {
        if (!IsMounted) return;
        if (IsRagdoll) return;

        IsMounted = false;
        IsRagdoll = true;
        ragdollTimer = ragdollDuration;
        remountTimer = 0f;

        // Активируем ActiveRagdoll
        if (activeRagdoll != null)
        {
            activeRagdoll.Activate();
        }

        // Активируем RagdollStabilizer (защита от провала)
        if (stabilizer != null)
        {
            stabilizer.Activate();
        }

        // Применяем импульс отделения
        if (characterRigidbody != null && !characterRigidbody.isKinematic)
        {
            characterRigidbody.useGravity = true;

            Vector3 impulse;

            if (customImpulse.HasValue)
            {
                impulse = customImpulse.Value;
            }
            else
            {
                // Считаем импульс: скорость лошади + вверх
                Vector3 horseVel = horseRigidbody != null ? horseRigidbody.velocity : Vector3.zero;
                impulse = horseVel * dismountForwardForce + Vector3.up * dismountUpForce;
            }

            // Применяем импульс к корневому Rigidbody
            characterRigidbody.AddForce(impulse, ForceMode.VelocityChange);

            // Добавляем случайный вращательный импульс (чтобы падение выглядело
            // естественно — персонаж кувыркается в воздухе)
            characterRigidbody.AddTorque(
                new Vector3(Random.Range(-2f, -0.5f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)),
                ForceMode.VelocityChange
            );
        }

        OnMountStateChanged?.Invoke(false);
    }

    /**
     * RecoverFromRagdoll — возврат персонажа в анимацию после падения.
     */
    public void RecoverFromRagdoll()
    {
        if (!IsRagdoll) return;

        IsRagdoll = false;
        canRemount = false;

        // Отключаем активный рагдол
        if (activeRagdoll != null) activeRagdoll.Deactivate();
        if (stabilizer != null) stabilizer.Deactivate();

        // Включаем обычную анимацию (Animator)
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.enabled = true;
            anim.SetTrigger("Recover");
        }

        OnRemountAvailable?.Invoke(true);  // Показываем кнопку "Сесть"
    }

    /**
     * TryMount — вызвать, если игрок нажал кнопку сесть (E).
     */
    public void TryMount()
    {
        if (IsMounted) return;
        if (IsRagdoll) return;

        if (Vector3.Distance(character.transform.position, horseRigidbody.position) < 2f)
        {
            Mount();
        }
    }

    // Обработчик столкновений — вешается на лошадь
    void OnCollisionEnter(Collision collision)
    {
        if (!IsMounted || IsRagdoll) return;

        // Проверяем силу столкновения
        float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;

        if (currentSpeed > dismountSpeedThreshold && impactForce > minImpulseForDismount)
        {
            // Получаем точку столкновения для направления импульса
            ContactPoint contact = collision.contacts[0];
            Vector3 impulseDir = (character.transform.position - contact.point).normalized;

            Dismount(impulseDir * currentSpeed * dismountForwardForce + Vector3.up * dismountUpForce);
        }
    }

    /**
     * OnCharacterHit — внешний вызов, когда персонажа ударили.
     * force — сила удара (например, 30 HP).
     */
    public void OnCharacterHit(float force, Vector3 hitDirection)
    {
        if (IsRagdoll) return;

        // Если персонаж на лошади и ударе > threshold — сбрасываем
        if (IsMounted && force > 30f)
        {
            Dismount(hitDirection * (force / 10f) + Vector3.up * dismountUpForce);
        }
        else if (!IsMounted && force > 30f)
        {
            // Обычный рагдол без лошади
            IsRagdoll = true;
            ragdollTimer = ragdollDuration;

            if (activeRagdoll != null) activeRagdoll.Activate();
            if (stabilizer != null) stabilizer.Activate();

            if (characterRigidbody != null)
            {
                characterRigidbody.AddForce(hitDirection * (force / 10f), ForceMode.Impulse);
            }
        }
    }
}
