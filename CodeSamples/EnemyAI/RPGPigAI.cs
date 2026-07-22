using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class RPGPigAI : MonoBehaviour, ILevelResettable
{
    [Header("Target Search")]
    [SerializeField] private string tankTag = "Tank";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool targetTankWhenPlayerIsInTank = true;
    [SerializeField] private float searchInterval = 0.5f;

    [Header("Detection")]
    [SerializeField] private bool autoAggroByDistance = true;
    [SerializeField] private float detectionRange = 45f;
    [SerializeField] private float attackRange = 30f;
    [SerializeField] private float lostRange = 60f;

    [Header("Panic Retreat")]
    [SerializeField] private bool usePanicRetreat = true;

    [Tooltip("Якщо true — панічний відступ працює тільки від кота, не від танка.")]
    [SerializeField] private bool panicRetreatOnlyFromPlayer = true;

    [Tooltip("Якщо кіт ближче цієї дистанції, RPG-свинка відбігає.")]
    [SerializeField] private float panicRetreatRange = 4f;

    [Tooltip("На яку відстань свинка намагається відбігти від кота.")]
    [SerializeField] private float panicRetreatDistance = 8f;

    [Tooltip("Скільки вбік може зміщуватися точка відступу, щоб рух був не ідеально назад.")]
    [SerializeField] private float panicSideStepAmount = 3f;

    [Tooltip("Максимальна тривалість panic retreat.")]
    [SerializeField] private float panicRetreatMaxDuration = 2.5f;

    [Tooltip("Наскільки близько треба підійти до точки відступу, щоб відступ завершився.")]
    [SerializeField] private float panicRetreatArriveDistance = 0.8f;

    [Tooltip("Маленька пауза після panic retreat, щоб не перезапускалось кожен кадр.")]
    [SerializeField] private float panicRetreatCooldown = 0.35f;

    [Tooltip("Скільки разів шукати валідну точку на NavMesh.")]
    [SerializeField] private int panicRetreatFindAttempts = 14;

    [Header("Random Combat Running")]
    [SerializeField] private bool useRandomCombatRunning = true;
    [SerializeField] private float randomRunIntervalMin = 5f;
    [SerializeField] private float randomRunIntervalMax = 10f;
    [SerializeField] private float randomRunRadius = 7f;
    [SerializeField] private float randomRunMinDistance = 2.5f;
    [SerializeField] private float randomRunMaxDuration = 3f;
    [SerializeField] private float randomRunArriveDistance = 0.8f;
    [SerializeField] private float randomRunAllowedDistanceMultiplier = 1.2f;
    [SerializeField] private float navMeshSampleDistance = 3f;

    [Header("Level Start Passive")]
    [SerializeField] private bool startPassiveOnLevelLoad = true;
    [SerializeField] private float passiveLockTimeAfterLevelStart = 2f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string aimingBool = "IsAiming";
    [SerializeField] private string shootingBool = "IsShooting";
    [SerializeField] private string turnAngleParameter = "TurnAngle";

    [Header("Turn In Place Like PigAI")]
    [SerializeField] private string shootingLayerName = "PigShootingLayer";
    [SerializeField] private string kneelLayerName = "PigKneelLayer";
    [SerializeField] private float timeToKneel = 0.8f;
    [SerializeField] private float weightSpeed = 15f;

    [Tooltip("False = як у звичайному PigAI: передаємо градуси. True = передаємо -1..1.")]
    [SerializeField] private bool normalizeTurnAngleToMinusOneOne = false;

    [Header("Shooting")]
    [SerializeField] private bool shootOnlyAfterKneel = false;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;
    [SerializeField] private bool debugTargetSwitch = true;
    [SerializeField] private bool debugAnimation = false;
    [SerializeField] private bool debugRandomRun = true;
    [SerializeField] private bool debugPanicRetreat = true;
    [SerializeField] private float debugInterval = 0.5f;

    private NavMeshAgent agent;
    private RPGPigWeaponSystem weaponSystem;
    private RPGPigHealth health;

    private Transform target;
    private bool aggressive;
    private float nextSearchTime;
    private float passiveLockedUntil;

    private Vector3 savedPosition;
    private Quaternion savedRotation;

    private float standingTimer;
    private float nextDebugTime;

    private int shootingLayerIndex = -1;
    private int kneelLayerIndex = -1;

    private bool hasSpeedParameter;
    private bool hasAimingBool;
    private bool hasShootingBool;
    private bool hasTurnAngleParameter;

    private float nextRandomRunTime;
    private bool isRandomRunning;
    private Vector3 randomRunDestination;
    private float randomRunEndTime;

    private bool isPanicRetreating;
    private Vector3 panicRetreatDestination;
    private float panicRetreatEndTime;
    private float nextPanicRetreatAllowedTime;

    private bool AgentReady =>
        agent != null &&
        agent.enabled &&
        agent.isOnNavMesh;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        weaponSystem = GetComponent<RPGPigWeaponSystem>();
        health = GetComponent<RPGPigHealth>();

        if (animator == null)
            animator = GetComponent<Animator>();

        CacheAnimatorData();

        savedPosition = transform.position;
        savedRotation = transform.rotation;
    }

    private void Start()
    {
        ScheduleNextRandomRun();

        if (startPassiveOnLevelLoad)
            ForcePassive(passiveLockTimeAfterLevelStart);

        if (debugMode)
            PrintAnimatorDebugInfo();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            StopMoving();
            SetIdleAnimation();
            StopRandomRun();
            StopPanicRetreat();
            return;
        }

        if (Time.time < passiveLockedUntil)
        {
            StopMoving();
            SetIdleAnimation();
            StopRandomRun();
            StopPanicRetreat();
            return;
        }

        SearchTargetIfNeeded();

        if (target == null)
        {
            StopMoving();
            SetIdleAnimation();
            StopRandomRun();
            StopPanicRetreat();
            return;
        }

        float distance = Vector3.Distance(transform.position, target.position);

        if (!aggressive && autoAggroByDistance && distance <= detectionRange)
        {
            aggressive = true;
            ScheduleNextRandomRun();

            if (debugMode)
            {
                Debug.Log(
                    "[RPGPigAI] Aggro by distance | Pig: " +
                    gameObject.name +
                    " | Target: " +
                    target.name
                );
            }
        }

        if (!aggressive)
        {
            StopMoving();
            SetIdleAnimation();
            StopRandomRun();
            StopPanicRetreat();
            return;
        }

        if (distance > lostRange)
        {
            ForcePassive(0f);
            return;
        }

        // Найвищий пріоритет: якщо кіт занадто близько — відбігти.
        if (HandlePanicRetreat(distance))
        {
            UpdateMovementAnimation();
            return;
        }

        // Другий пріоритет: звичайний випадковий рух у бою.
        if (HandleRandomCombatRunning(distance))
        {
            UpdateMovementAnimation();
            return;
        }

        if (distance > attackRange)
        {
            MoveToTarget();
        }
        else
        {
            StopAndShoot();
        }

        UpdateMovementAnimation();
    }

    public void ActivateAggression()
    {
        if (health != null && health.IsDead)
            return;

        aggressive = true;
        passiveLockedUntil = 0f;
        SearchTargetNow();
        ScheduleNextRandomRun();

        if (debugMode)
        {
            Debug.Log(
                "[RPGPigAI] Aggro manually | Pig: " +
                gameObject.name +
                " | Target: " +
                (target != null ? target.name : "NULL")
            );
        }
    }

    public void ForcePassive(float lockTime)
    {
        aggressive = false;
        target = null;
        nextSearchTime = Time.time + searchInterval;
        passiveLockedUntil = Time.time + Mathf.Max(0f, lockTime);
        standingTimer = 0f;

        StopRandomRun();
        StopPanicRetreat();
        StopMoving();
        SetIdleAnimation();
        ScheduleNextRandomRun();

        if (debugMode)
            Debug.Log("[RPGPigAI] Forced passive: " + gameObject.name);
    }

    private void SearchTargetIfNeeded()
    {
        if (Time.time < nextSearchTime)
            return;

        nextSearchTime = Time.time + searchInterval;
        SearchTargetNow();
    }

    private void SearchTargetNow()
    {
        Transform newTarget = SelectCombatTarget();

        if (newTarget == target)
            return;

        target = newTarget;

        if (target != null)
            ScheduleNextRandomRun();

        if (debugTargetSwitch)
        {
            Debug.Log(
                "[RPGPigAI] Target switched | Pig: " +
                gameObject.name +
                " | Target: " +
                (target != null ? target.name : "NULL") +
                " | playerIsInTank: " +
                IsPlayerInTank()
            );
        }
    }

    private Transform SelectCombatTarget()
    {
        if (targetTankWhenPlayerIsInTank && IsPlayerInTank())
        {
            Transform tankTarget = FindTankTarget();

            if (tankTarget != null)
                return tankTarget;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
            return playerObject.transform;

        return FindTankTarget();
    }

    private bool IsPlayerInTank()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.playerIsInTank;
    }

    private Transform FindTankTarget()
    {
        TankHealth tankHealth = FindFirstObjectByType<TankHealth>();

        if (tankHealth != null)
        {
            if (!tankHealth.IsDead)
                return tankHealth.transform;

            return null;
        }

        GameObject tankObject = GameObject.FindGameObjectWithTag(tankTag);

        if (tankObject != null)
            return tankObject.transform;

        return null;
    }

    private bool HandlePanicRetreat(float distanceToTarget)
    {
        if (!usePanicRetreat)
            return false;

        if (!AgentReady || target == null)
            return false;

        if (isPanicRetreating)
        {
            ContinuePanicRetreat();
            return true;
        }

        if (Time.time < nextPanicRetreatAllowedTime)
            return false;

        if (distanceToTarget > panicRetreatRange)
            return false;

        if (panicRetreatOnlyFromPlayer && !IsCurrentTargetPlayer())
            return false;

        return TryStartPanicRetreat();
    }

    private bool IsCurrentTargetPlayer()
    {
        if (target == null)
            return false;

        return target.CompareTag(playerTag);
    }

    private bool TryStartPanicRetreat()
    {
        if (!AgentReady || target == null)
            return false;

        Vector3 awayDirection = transform.position - target.position;
        awayDirection.y = 0f;

        if (awayDirection.sqrMagnitude < 0.01f)
            awayDirection = -transform.forward;

        awayDirection.Normalize();

        Vector3 sideDirection = Vector3.Cross(Vector3.up, awayDirection).normalized;
        float currentDistanceToTarget = Vector3.Distance(transform.position, target.position);

        for (int i = 0; i < panicRetreatFindAttempts; i++)
        {
            float retreatDistance = Random.Range(
                panicRetreatDistance * 0.65f,
                panicRetreatDistance
            );

            float sideOffset = Random.Range(
                -panicSideStepAmount,
                panicSideStepAmount
            );

            Vector3 candidate =
                transform.position +
                awayDirection * retreatDistance +
                sideDirection * sideOffset;

            if (NavMesh.SamplePosition(
                candidate,
                out NavMeshHit hit,
                navMeshSampleDistance,
                NavMesh.AllAreas
            ))
            {
                float newDistanceToTarget = Vector3.Distance(hit.position, target.position);

                if (newDistanceToTarget <= currentDistanceToTarget + 1f)
                    continue;

                panicRetreatDestination = hit.position;
                panicRetreatEndTime = Time.time + panicRetreatMaxDuration;
                isPanicRetreating = true;
                isRandomRunning = false;

                agent.updateRotation = true;
                agent.isStopped = false;
                agent.ResetPath();
                agent.SetDestination(panicRetreatDestination);

                standingTimer = 0f;

                SetAiming(false);
                SetShooting(false);
                SetTurnAngle(0f);

                UpdateLayerWeight(shootingLayerIndex, 0f);
                UpdateLayerWeight(kneelLayerIndex, 0f);

                if (debugPanicRetreat)
                {
                    Debug.Log(
                        "[RPGPigAI] PANIC RETREAT STARTED | Pig: " +
                        gameObject.name +
                        " | From target: " +
                        target.name +
                        " | Destination: " +
                        panicRetreatDestination
                    );
                }

                return true;
            }
        }

        nextPanicRetreatAllowedTime = Time.time + panicRetreatCooldown;

        if (debugPanicRetreat)
        {
            Debug.LogWarning(
                "[RPGPigAI] Panic retreat failed to find NavMesh point | Pig: " +
                gameObject.name
            );
        }

        return false;
    }

    private void ContinuePanicRetreat()
    {
        if (!AgentReady)
        {
            FinishPanicRetreat("agent not ready");
            return;
        }

        SetAiming(false);
        SetShooting(false);
        SetTurnAngle(0f);

        UpdateLayerWeight(shootingLayerIndex, 0f);
        UpdateLayerWeight(kneelLayerIndex, 0f);

        if (!agent.pathPending)
        {
            if (agent.remainingDistance <= panicRetreatArriveDistance)
            {
                FinishPanicRetreat("arrived");
                return;
            }
        }

        if (Time.time >= panicRetreatEndTime)
        {
            FinishPanicRetreat("timeout");
            return;
        }

        if (!agent.hasPath)
        {
            FinishPanicRetreat("no path");
            return;
        }
    }

    private void FinishPanicRetreat(string reason)
    {
        isPanicRetreating = false;
        standingTimer = 0f;
        nextPanicRetreatAllowedTime = Time.time + panicRetreatCooldown;

        if (AgentReady)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.updateRotation = false;
        }

        ScheduleNextRandomRun();

        if (debugPanicRetreat)
        {
            Debug.Log(
                "[RPGPigAI] PANIC RETREAT FINISHED | Pig: " +
                gameObject.name +
                " | Reason: " +
                reason
            );
        }
    }

    private void StopPanicRetreat()
    {
        isPanicRetreating = false;
    }

    private bool HandleRandomCombatRunning(float distanceToTarget)
    {
        if (!useRandomCombatRunning)
            return false;

        if (!AgentReady || target == null)
            return false;

        float allowedDistance = attackRange * randomRunAllowedDistanceMultiplier;

        if (distanceToTarget > allowedDistance)
        {
            if (isRandomRunning)
                StopRandomRun();

            return false;
        }

        if (isRandomRunning)
        {
            ContinueRandomRun();
            return true;
        }

        if (Time.time < nextRandomRunTime)
            return false;

        return TryStartRandomRun();
    }

    private bool TryStartRandomRun()
    {
        if (!AgentReady)
            return false;

        for (int i = 0; i < 16; i++)
        {
            Vector3 randomDirection = GetRandomDirectionXZ();
            float distance = Random.Range(randomRunMinDistance, randomRunRadius);

            Vector3 candidate = transform.position + randomDirection * distance;

            if (NavMesh.SamplePosition(
                candidate,
                out NavMeshHit hit,
                navMeshSampleDistance,
                NavMesh.AllAreas
            ))
            {
                randomRunDestination = hit.position;
                randomRunEndTime = Time.time + randomRunMaxDuration;
                isRandomRunning = true;

                agent.updateRotation = true;
                agent.isStopped = false;
                agent.ResetPath();
                agent.SetDestination(randomRunDestination);

                standingTimer = 0f;

                SetAiming(false);
                SetShooting(false);
                SetTurnAngle(0f);

                UpdateLayerWeight(shootingLayerIndex, 0f);
                UpdateLayerWeight(kneelLayerIndex, 0f);

                if (debugRandomRun)
                {
                    Debug.Log(
                        "[RPGPigAI] Random run started | Pig: " +
                        gameObject.name +
                        " | Destination: " +
                        randomRunDestination
                    );
                }

                return true;
            }
        }

        ScheduleNextRandomRun();

        if (debugRandomRun)
        {
            Debug.LogWarning(
                "[RPGPigAI] Failed to find random NavMesh point | Pig: " +
                gameObject.name
            );
        }

        return false;
    }

    private Vector3 GetRandomDirectionXZ()
    {
        Vector2 randomCircle = Random.insideUnitCircle;

        if (randomCircle.sqrMagnitude < 0.01f)
            randomCircle = Vector2.right;

        randomCircle.Normalize();

        return new Vector3(randomCircle.x, 0f, randomCircle.y);
    }

    private void ContinueRandomRun()
    {
        if (!AgentReady)
        {
            StopRandomRun();
            return;
        }

        SetAiming(false);
        SetShooting(false);
        SetTurnAngle(0f);

        UpdateLayerWeight(shootingLayerIndex, 0f);
        UpdateLayerWeight(kneelLayerIndex, 0f);

        if (!agent.pathPending)
        {
            if (agent.remainingDistance <= randomRunArriveDistance)
            {
                FinishRandomRun("arrived");
                return;
            }
        }

        if (Time.time >= randomRunEndTime)
        {
            FinishRandomRun("timeout");
            return;
        }

        if (!agent.hasPath)
        {
            FinishRandomRun("no path");
            return;
        }
    }

    private void FinishRandomRun(string reason)
    {
        isRandomRunning = false;
        standingTimer = 0f;

        if (AgentReady)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.updateRotation = false;
        }

        ScheduleNextRandomRun();

        if (debugRandomRun)
        {
            Debug.Log(
                "[RPGPigAI] Random run finished | Pig: " +
                gameObject.name +
                " | Reason: " +
                reason +
                " | Next random run in: " +
                (nextRandomRunTime - Time.time).ToString("F1") +
                " sec"
            );
        }
    }

    private void StopRandomRun()
    {
        isRandomRunning = false;
    }

    private void ScheduleNextRandomRun()
    {
        float min = Mathf.Min(randomRunIntervalMin, randomRunIntervalMax);
        float max = Mathf.Max(randomRunIntervalMin, randomRunIntervalMax);

        nextRandomRunTime = Time.time + Random.Range(min, max);
    }

    private void MoveToTarget()
    {
        if (!AgentReady || target == null)
            return;

        agent.updateRotation = true;
        agent.isStopped = false;
        agent.SetDestination(target.position);

        standingTimer = 0f;

        SetAiming(false);
        SetShooting(false);
        SetTurnAngle(0f);

        UpdateLayerWeight(shootingLayerIndex, 0f);
        UpdateLayerWeight(kneelLayerIndex, 0f);
    }

    private void StopAndShoot()
    {
        if (!AgentReady)
            return;

        agent.isStopped = true;
        agent.ResetPath();
        agent.updateRotation = false;

        standingTimer += Time.deltaTime;

        float angle = RotateTowardsTarget();

        SetAiming(true);
        SetShooting(true);
        HandleAnimationLayers();

        bool canShoot = !shootOnlyAfterKneel || standingTimer >= timeToKneel;

        if (canShoot && weaponSystem != null && target != null)
            weaponSystem.TryShoot(target);

        DebugAnimationState(angle);
    }

    private void StopMoving()
    {
        if (!AgentReady)
            return;

        agent.isStopped = true;
        agent.ResetPath();
    }

    private float RotateTowardsTarget()
    {
        if (target == null)
            return 0f;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            SetTurnAngle(0f);
            return 0f;
        }

        direction.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float angle = Vector3.SignedAngle(transform.forward, direction, Vector3.up);

        float valueForAnimator = angle;

        if (normalizeTurnAngleToMinusOneOne)
            valueForAnimator = Mathf.Clamp(angle / 90f, -1f, 1f);

        SetTurnAngle(valueForAnimator);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );

        return angle;
    }

    private void HandleAnimationLayers()
    {
        float targetKneel = standingTimer >= timeToKneel ? 1f : 0f;
        float targetShoot = standingTimer >= timeToKneel ? 0f : 1f;

        UpdateLayerWeight(kneelLayerIndex, targetKneel);
        UpdateLayerWeight(shootingLayerIndex, targetShoot);
    }

    private void UpdateMovementAnimation()
    {
        float speed = 0f;

        if (AgentReady)
            speed = agent.velocity.magnitude;

        SetSpeed(speed);
    }

    private void SetIdleAnimation()
    {
        standingTimer = 0f;

        SetSpeed(0f);
        SetAiming(false);
        SetShooting(false);
        SetTurnAngle(0f);

        UpdateLayerWeight(kneelLayerIndex, 0f);
        UpdateLayerWeight(shootingLayerIndex, 0f);
    }

    private void CacheAnimatorData()
    {
        hasSpeedParameter = false;
        hasAimingBool = false;
        hasShootingBool = false;
        hasTurnAngleParameter = false;

        shootingLayerIndex = -1;
        kneelLayerIndex = -1;

        if (animator == null)
        {
            Debug.LogError("[RPGPigAI] Animator is missing on: " + gameObject.name);
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == speedParameter)
                hasSpeedParameter = true;

            if (parameter.name == aimingBool)
                hasAimingBool = true;

            if (parameter.name == shootingBool)
                hasShootingBool = true;

            if (parameter.name == turnAngleParameter)
                hasTurnAngleParameter = true;
        }

        shootingLayerIndex = animator.GetLayerIndex(shootingLayerName);
        kneelLayerIndex = animator.GetLayerIndex(kneelLayerName);
    }

    private void SetSpeed(float value)
    {
        if (animator == null || !hasSpeedParameter)
            return;

        animator.SetFloat(speedParameter, value);
    }

    private void SetAiming(bool value)
    {
        if (animator == null || !hasAimingBool)
            return;

        animator.SetBool(aimingBool, value);
    }

    private void SetShooting(bool value)
    {
        if (animator == null || !hasShootingBool)
            return;

        animator.SetBool(shootingBool, value);
    }

    private void SetTurnAngle(float value)
    {
        if (animator == null || !hasTurnAngleParameter)
            return;

        animator.SetFloat(turnAngleParameter, value);
    }

    private void UpdateLayerWeight(int layerIndex, float targetWeight)
    {
        if (animator == null)
            return;

        if (layerIndex < 0)
            return;

        float currentWeight = animator.GetLayerWeight(layerIndex);

        float newWeight = Mathf.MoveTowards(
            currentWeight,
            targetWeight,
            Time.deltaTime * weightSpeed
        );

        animator.SetLayerWeight(layerIndex, newWeight);
    }

    private void PrintAnimatorDebugInfo()
    {
        if (animator == null)
            return;

        Debug.Log(
            "========== RPGPigAI Animator Debug ==========\n" +
            "Pig: " + gameObject.name + "\n" +
            "Animator: " + animator.name + "\n" +
            "Controller: " + (animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULL") + "\n" +
            "Has Speed: " + hasSpeedParameter + "\n" +
            "Has IsAiming: " + hasAimingBool + "\n" +
            "Has IsShooting: " + hasShootingBool + "\n" +
            "Has TurnAngle: " + hasTurnAngleParameter + "\n" +
            "Shooting Layer Name: " + shootingLayerName + "\n" +
            "Shooting Layer Index: " + shootingLayerIndex + "\n" +
            "Kneel Layer Name: " + kneelLayerName + "\n" +
            "Kneel Layer Index: " + kneelLayerIndex + "\n" +
            "Panic Retreat: " + usePanicRetreat + "\n" +
            "Panic Retreat Range: " + panicRetreatRange + "\n" +
            "Random Run: " + useRandomCombatRunning + "\n" +
            "Random Run Interval: " + randomRunIntervalMin + " - " + randomRunIntervalMax + "\n" +
            "============================================"
        );
    }

    private void DebugAnimationState(float angle)
    {
        if (!debugAnimation)
            return;

        if (Time.time < nextDebugTime)
            return;

        nextDebugTime = Time.time + debugInterval;

        float kneelWeight = -1f;
        float shootingWeight = -1f;
        float turnAngle = -999f;

        if (animator != null)
        {
            if (kneelLayerIndex >= 0)
                kneelWeight = animator.GetLayerWeight(kneelLayerIndex);

            if (shootingLayerIndex >= 0)
                shootingWeight = animator.GetLayerWeight(shootingLayerIndex);

            if (hasTurnAngleParameter)
                turnAngle = animator.GetFloat(turnAngleParameter);
        }

        Debug.Log(
            "[RPGPigAI ANIM DEBUG] " + gameObject.name +
            "\nTarget: " + (target != null ? target.name : "NULL") +
            "\nStandingTimer: " + standingTimer.ToString("F2") +
            "\nRaw Angle: " + angle.ToString("F2") +
            "\nAnimator TurnAngle: " + turnAngle.ToString("F2") +
            "\nKneelLayer Index: " + kneelLayerIndex +
            "\nKneelLayer Weight: " + kneelWeight.ToString("F2") +
            "\nShootingLayer Index: " + shootingLayerIndex +
            "\nShootingLayer Weight: " + shootingWeight.ToString("F2") +
            "\nIsRandomRunning: " + isRandomRunning +
            "\nIsPanicRetreating: " + isPanicRetreating
        );
    }

    public void SaveLevelStartState()
    {
        savedPosition = transform.position;
        savedRotation = transform.rotation;

        aggressive = false;
        target = null;
        standingTimer = 0f;

        StopRandomRun();
        StopPanicRetreat();

        if (debugMode)
            Debug.Log("[RPGPigAI] Level start state saved as PASSIVE: " + gameObject.name);
    }

    public void RestoreLevelStartState()
    {
        aggressive = false;
        target = null;
        standingTimer = 0f;

        StopRandomRun();
        StopPanicRetreat();

        if (AgentReady)
        {
            agent.Warp(savedPosition);
            agent.ResetPath();
            agent.isStopped = true;
            agent.updateRotation = true;
        }

        transform.position = savedPosition;
        transform.rotation = savedRotation;

        ForcePassive(passiveLockTimeAfterLevelStart);

        if (debugMode)
            Debug.Log("[RPGPigAI] Restored to PASSIVE: " + gameObject.name);
    }
}
