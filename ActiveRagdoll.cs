using UnityEngine;

/**
 * ActiveRagdoll — активный рагдол (как Euphoria в GTA V / RDR2).
 *
 * ПРИНЦИП: Каждая кость управляется через ConfigurableJoint.
 * Joint пытается вернуться в целевую позу (defense pose) с настраиваемой
 * жёсткостью (spring), демпфированием (damper) и максимальной силой (maxForce).
 *
 * В отличие от пассивного ragdoll, где тело тряпичное, здесь:
 * - Мышцы напряжены — тело упругое, конечности сопротивляются.
 * - При ударе о землю руки выставляются вперёд (смягчение падения).
 * - После падения тело пытается принять защитную позу.
 *
 * Все параметры регулируются в инспекторе.
 */
public class ActiveRagdoll : MonoBehaviour
{
    [System.Serializable]
    public class JointSettings
    {
        [Tooltip("Жёсткость мышцы (чем выше, тем сильнее тянет в целевую позу)")]
        public float muscleSpring = 1000f;
        [Tooltip("Демпфирование (гасит колебания)")]
        public float muscleDamper = 100f;
        [Tooltip("Максимальная сила, которую может приложить joint")]
        public float maxForce = 5000f;
    }

    [Header("Глобальные настройки активного рагдола")]
    [Tooltip("Включить/выключить активный рагдол")]
    public bool enableActiveRagdoll = true;

    public JointSettings jointSettings = new JointSettings();

    [Header("Защитная поза (углы в градусах)")]
    [Tooltip("Наклон головы вперёд (X)")]
    public float headTiltX = 15f;
    [Tooltip("Подъём плеч (X) — руки к лицу")]
    public float armLiftX = -45f;
    [Tooltip("Сгиб локтей (Z)")]
    public float elbowBendZ = -60f;
    [Tooltip("Сгиб коленей (X)")]
    public float kneeBendX = 30f;

    [Header("Смягчение падения")]
    [Tooltip("Дистанция до пола, при которой руки выставляются")]
    public float handProtectDistance = 0.5f;
    [Tooltip("Дополнительный угол для рук при падении")]
    public float handProtectAngle = -30f;

    private ConfigurableJoint[] joints;
    private Rigidbody[] bones;
    private Quaternion[] defaultRotations;  // Локальные rotations костей
    private bool isActive = false;

    // Ссылки на конкретные кости для защиты
    private Transform leftArm;
    private Transform rightArm;
    private Transform head;
    private Transform spine;

    void Awake()
    {
        joints = GetComponentsInChildren<ConfigurableJoint>();
        bones = GetComponentsInChildren<Rigidbody>();

        // Сохраняем дефолтные локальные вращения костей
        defaultRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            defaultRotations[i] = bones[i].transform.localRotation;
        }

        // Находим важные кости по имени (Humanoid bone names)
        FindBones();

        // Настраиваем все ConfigurableJoint
        foreach (var joint in joints)
        {
            SetupJoint(joint);
        }
    }

    private void FindBones()
    {
        // Ищем по иерархии типичные Humanoid-кости
        foreach (var rb in bones)
        {
            string name = rb.name.ToLower();
            if (name.Contains("head")) head = rb.transform;
            if (name.Contains("leftarm") || name.Contains("left_arm") || name.Contains("upper_arm_left"))
                leftArm = rb.transform;
            if (name.Contains("rightarm") || name.Contains("right_arm") || name.Contains("upper_arm_right"))
                rightArm = rb.transform;
            if (name.Contains("spine")) spine = rb.transform;
        }
    }

    private void SetupJoint(ConfigurableJoint joint)
    {
        // Настройка joint для активного рагдола
        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularZMotion = ConfigurableJointMotion.Limited;

        // Настройки пружины
        var drive = joint.slerpDrive;
        drive.positionSpring = jointSettings.muscleSpring;
        drive.positionDamper = jointSettings.muscleDamper;
        drive.maximumForce = jointSettings.maxForce;
        joint.slerpDrive = drive;
        joint.rotationDriveMode = RotationDriveMode.Slerp;

        // Лимиты: ограничиваем, чтобы кости не выкручивались
        var limit = joint.angularYLimit;
        limit.limit = 45f;
        joint.angularYLimit = limit;
    }

    public void Activate()
    {
        isActive = true;
        if (joints == null) return;

        foreach (var joint in joints)
        {
            joint.enableCollision = true;
        }

        // Стартуем с защитной позы
        SetDefensePose();
    }

    public void Deactivate()
    {
        isActive = false;

        // Плавно возвращаем кости в дефолт
        if (joints == null) return;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
            {
                bones[i].transform.localRotation = defaultRotations[i];
            }
        }
    }

    void LateUpdate()
    {
        if (!isActive || !enableActiveRagdoll || joints == null) return;

        // Проверка на падение: если летим вниз и скоро удар
        CheckFallProtection();

        // Применяем целевую позу к каждому joint
        ApplyDefensePose();
    }

    private void CheckFallProtection()
    {
        if (head == null || leftArm == null || rightArm == null) return;

        // Raycast вниз от головы/таза
        RaycastHit hit;
        Vector3 origin = head.position;
        float rayDist = handProtectDistance + 0.5f;

        if (Physics.Raycast(origin, Vector3.down, out hit, rayDist))
        {
            if (hit.distance < handProtectDistance)
            {
                // Выставляем руки вперёд для смягчения
                float blend = 1f - (hit.distance / handProtectDistance);

                if (leftArm != null)
                {
                    Quaternion protectRot = Quaternion.Euler(
                        handProtectAngle * blend, 0, leftArm.localRotation.eulerAngles.z);
                    SetTargetRotation(leftArm, protectRot);
                }

                if (rightArm != null)
                {
                    Quaternion protectRot = Quaternion.Euler(
                        handProtectAngle * blend, 0, rightArm.localRotation.eulerAngles.z);
                    SetTargetRotation(rightArm, protectRot);
                }
            }
        }
    }

    private void SetDefensePose()
    {
        if (head != null)
            SetTargetRotation(head, Quaternion.Euler(headTiltX, 0, 0));

        if (leftArm != null)
            SetTargetRotation(leftArm, Quaternion.Euler(armLiftX, 0, elbowBendZ));

        if (rightArm != null)
            SetTargetRotation(rightArm, Quaternion.Euler(armLiftX, 0, -elbowBendZ));
    }

    private void ApplyDefensePose()
    {
        if (head != null)
            DriveToRotation(head, Quaternion.Euler(headTiltX, 0, 0));

        if (leftArm != null)
            DriveToRotation(leftArm, Quaternion.Euler(armLiftX, 0, elbowBendZ));

        if (rightArm != null)
            DriveToRotation(rightArm, Quaternion.Euler(armLiftX, 0, -elbowBendZ));
    }

    private void SetTargetRotation(Transform bone, Quaternion targetLocalRot)
    {
        // Устанавливаем целевую ротацию для ConfigurableJoint
        var joint = bone.GetComponent<ConfigurableJoint>();
        if (joint == null) return;

        // Цель задаётся в пространстве родителя
        joint.targetRotation = targetLocalRot;
    }

    private void DriveToRotation(Transform bone, Quaternion targetLocalRot)
    {
        // Альтернативный метод: напрямую через Rigidbody.MoveRotation
        var rb = bone.GetComponent<Rigidbody>();
        if (rb == null) return;

        Quaternion worldTarget = rb.transform.parent.rotation * targetLocalRot;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, worldTarget, 0.1f));
    }

    // Применить внешнюю силу к конкретной кости
    public void ApplyForceToBone(string boneName, Vector3 force, ForceMode mode = ForceMode.Impulse)
    {
        foreach (var rb in bones)
        {
            if (rb.name.ToLower().Contains(boneName.ToLower()))
            {
                rb.AddForce(force, mode);
                return;
            }
        }
    }

    // Применить силу ко всем костям (например, взрыв)
    public void ApplyForceToAll(Vector3 force, ForceMode mode = ForceMode.Impulse)
    {
        foreach (var rb in bones)
        {
            rb.AddForce(force, mode);
        }
    }
}
