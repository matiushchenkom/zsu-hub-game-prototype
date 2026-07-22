using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using System.Collections;

public class InventoryManager : MonoBehaviour, ILevelResettable, IPlayerSaveParticipant
{
    [Header("Infrastructure")]
    private CharacterMountPoints mountPoints;
    private MovementStateManager movementScript;
    private CatHealth catHealth;

    public DroneController droneScript;
    public Animator catAnimator;

    [Header("UI & Interaction")]
    public GameObject inventoryPanel;
    public GameObject interactionPromptUI;
    public TextMeshProUGUI promptText;
    public TextMeshProUGUI statusText;
    public Sprite defaultEmptyIcon;
    public float interactionRange = 3f;
    public LayerMask interactableLayer;

    [Header("UI Reset Safety")]
    [SerializeField] private bool forceInventoryPanelVisibleAfterRestart = true;

    [Header("Slot 1 (Rifle)")]
    public GameObject weapon1InHand;
    public GameObject slot1Object;
    public Image rifleIcon;
    public bool isSlot1Full = true;

    [Header("Slot 2 (Dynamic)")]
    public GameObject weapon2InHand;
    public GameObject slot2Object;
    public Image slot2Icon;
    public TextMeshProUGUI slot2CountText;
    private GameObject slot2WorldPrefab;
    private GameObject slot2HandModelPrefab;
    public bool isSlot2Full = false;
    private int slot2Count = 0;

    [Header("Slot 3 (Dynamic)")]
    public GameObject weapon3InHand;
    public GameObject slot3Object;
    public Image slot3Icon;
    public TextMeshProUGUI slot3CountText;
    private GameObject slot3WorldPrefab;
    private GameObject slot3HandModelPrefab;
    public bool isSlot3Full = false;
    private int slot3Count = 0;

    [Header("Settings")]
    public int maxStackSize = 5;

    [Header("Ammo & HUD")]
    public GameObject ammoHUDPanel;
    public TextMeshProUGUI currentAmmoText;
    public TextMeshProUGUI reservedAmmoText;
    public GameObject reloadPrompt;
    public int currentAmmo = 30;
    public int maxMagSize = 30;
    public int reservedAmmo = 60;
    public float reloadDuration = 2.0f;

    [Header("Movement Settings")]
    public float normalWalkSpeed = 4f;
    public float normalRunSpeed = 7f;
    public float combatWalkSpeed = 2f;
    public float combatRunSpeed = 5f;

    [Header("Combat Settings")]
    [SerializeField] private float fireRate = 0.15f;
    public Transform firePoint;
    public GameObject muzzleFlashPrefab;
    public GameObject tracerPrefab;
    public GameObject impactEffect;
    public LayerMask shootableLayers;
    public float range = 100f;
    public float tracerSpeed = 100f;

    [Header("Rigging & Layers")]
    public Rig aimRig;
    public MultiAimConstraint aimConstraint;
    public string combatLayerName = "CombatLayer";
    public string shootingLayerName = "ShootingLayer";
    public string weaponTypeParam = "WeaponType";
    public string equipTriggerParam = "EquipTrigger";
    [Range(0.05f, 0.4f)] public float transitionDuration = 0.15f;

    [Header("Slope / Jump Visual Buffer")]
    [SerializeField] private float groundResetDelay = 0.05f;

    private int currentSelectedSlot = -1;
    private bool isReloading = false;
    private bool isUsingItem = false;
    private float fireTimer;
    private int combatLayerIndex;
    private int shootingLayerIndex;
    private Coroutine reloadCoroutine;
    private Coroutine weightCoroutine;
    private float lostGroundTimer;

    private int savedCurrentAmmo;
    private int savedReservedAmmo;
    private int savedSelectedSlot;

    private bool savedSlot2Full;
    private int savedSlot2Count;
    private GameObject savedSlot2WorldPrefab;
    private GameObject savedSlot2HandModelPrefab;
    private Sprite savedSlot2Icon;

    private bool savedSlot3Full;
    private int savedSlot3Count;
    private GameObject savedSlot3WorldPrefab;
    private GameObject savedSlot3HandModelPrefab;
    private Sprite savedSlot3Icon;

    [Header("Save System")]
    [SerializeField] private SaveableInventoryItem[] saveableItems;

    private int defaultCurrentAmmo;
    private int defaultReservedAmmo;

    private void Start()
    {
        movementScript = GetComponent<MovementStateManager>();
        mountPoints = GetComponent<CharacterMountPoints>();
        catHealth = GetComponent<CatHealth>();

        defaultCurrentAmmo = currentAmmo;
        defaultReservedAmmo = reservedAmmo;

        if (catAnimator != null)
        {
            combatLayerIndex = catAnimator.GetLayerIndex(combatLayerName);
            shootingLayerIndex = catAnimator.GetLayerIndex(shootingLayerName);
        }

        UpdateAmmoUI();

        if (interactionPromptUI != null)
            interactionPromptUI.SetActive(false);

        if (reloadPrompt != null)
            reloadPrompt.SetActive(false);

        if (statusText != null)
            statusText.text = "";

        SetInventoryPanelVisibleForPlayer();
        DeselectAll();
    }

    private void Update()
    {
        if (droneScript != null && droneScript.isActive)
            return;

        if (isUsingItem)
            return;

        UpdateGroundVisualTimer();
        UpdateInteractionPrompt();

        if (!isReloading)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) ToggleSlot(1);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) ToggleSlot(2);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) ToggleSlot(3);

            if (Keyboard.current.eKey.wasPressedThisFrame) TryPickupItem();
            if (Keyboard.current.gKey.wasPressedThisFrame) DropCurrentItem();
            if (Keyboard.current.rKey.wasPressedThisFrame && currentSelectedSlot == 1) Reload();

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (currentSelectedSlot > 1)
                    TryUseItem();
            }
        }

        HandleJumpLogic();
        UpdateMovementSpeed(currentSelectedSlot != -1);

        bool visuallyGrounded = IsVisuallyGroundedForCombat();

        if (currentSelectedSlot == 1 && movementScript != null && visuallyGrounded && !isReloading)
        {
            if (Mouse.current.leftButton.isPressed && fireTimer <= 0f)
            {
                Fire();
                fireTimer = fireRate;
            }
        }

        if (fireTimer > 0f)
            fireTimer -= Time.deltaTime;
    }

    public void SaveLevelStartState()
    {
        savedCurrentAmmo = currentAmmo;
        savedReservedAmmo = reservedAmmo;
        savedSelectedSlot = currentSelectedSlot;

        savedSlot2Full = isSlot2Full;
        savedSlot2Count = slot2Count;
        savedSlot2WorldPrefab = slot2WorldPrefab;
        savedSlot2HandModelPrefab = slot2HandModelPrefab;
        savedSlot2Icon = slot2Icon != null ? slot2Icon.sprite : null;

        savedSlot3Full = isSlot3Full;
        savedSlot3Count = slot3Count;
        savedSlot3WorldPrefab = slot3WorldPrefab;
        savedSlot3HandModelPrefab = slot3HandModelPrefab;
        savedSlot3Icon = slot3Icon != null ? slot3Icon.sprite : null;
    }

    public void RestoreLevelStartState()
    {
        StopAllCoroutines();
        reloadCoroutine = null;
        weightCoroutine = null;

        isReloading = false;
        isUsingItem = false;
        fireTimer = 0f;

        ClearTemporaryUI();

        currentAmmo = savedCurrentAmmo;
        reservedAmmo = savedReservedAmmo;

        if (weapon2InHand != null)
            Destroy(weapon2InHand);

        if (weapon3InHand != null)
            Destroy(weapon3InHand);

        isSlot2Full = savedSlot2Full;
        slot2Count = savedSlot2Count;
        slot2WorldPrefab = savedSlot2WorldPrefab;
        slot2HandModelPrefab = savedSlot2HandModelPrefab;
        weapon2InHand = RecreateHandModel(slot2HandModelPrefab);

        isSlot3Full = savedSlot3Full;
        slot3Count = savedSlot3Count;
        slot3WorldPrefab = savedSlot3WorldPrefab;
        slot3HandModelPrefab = savedSlot3HandModelPrefab;
        weapon3InHand = RecreateHandModel(slot3HandModelPrefab);

        if (slot2Icon != null)
            slot2Icon.sprite = isSlot2Full && savedSlot2Icon != null ? savedSlot2Icon : defaultEmptyIcon;

        if (slot3Icon != null)
            slot3Icon.sprite = isSlot3Full && savedSlot3Icon != null ? savedSlot3Icon : defaultEmptyIcon;

        currentSelectedSlot = -1;

        if (IsSlotAvailable(savedSelectedSlot))
            SelectSlot(savedSelectedSlot);
        else
            DeselectAll();

        UpdateWeaponVisibility();
        SetInventoryPanelVisibleForPlayer();
        UpdateAmmoUI();
        UpdateUI();
    }

    private GameObject RecreateHandModel(GameObject handModelPrefab)
    {
        if (handModelPrefab == null)
            return null;

        if (mountPoints == null)
            mountPoints = GetComponent<CharacterMountPoints>();

        if (mountPoints == null || mountPoints.rightHandSocket == null)
            return null;

        GameObject model = Instantiate(handModelPrefab, mountPoints.rightHandSocket);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.SetActive(false);

        return model;
    }

    private bool IsSlotAvailable(int slot)
    {
        if (slot == 1)
            return true;

        if (slot == 2)
            return isSlot2Full;

        if (slot == 3)
            return isSlot3Full;

        return false;
    }

    private void UpdateGroundVisualTimer()
    {
        if (movementScript == null)
            return;

        if (movementScript.IsJumpLocked)
        {
            lostGroundTimer = 0f;
            return;
        }

        if (movementScript.IsGrounded)
            lostGroundTimer = groundResetDelay;
        else
            lostGroundTimer -= Time.deltaTime;
    }

    private bool IsVisuallyGroundedForCombat()
    {
        if (movementScript == null)
            return false;

        if (movementScript.IsJumpLocked)
            return false;

        return lostGroundTimer > 0f;
    }

    private void HandleJumpLogic()
    {
        if (movementScript == null || catAnimator == null)
            return;

        if (isReloading)
            return;

        if (isUsingItem)
        {
            UpdateWeightsDirectly(1f);

            GameObject eatingWeapon = GetCurrentWeaponModel();

            if (eatingWeapon != null)
                eatingWeapon.SetActive(true);

            return;
        }

        bool visuallyGrounded = IsVisuallyGroundedForCombat();

        if (currentSelectedSlot != -1)
        {
            GameObject activeWeapon = GetCurrentWeaponModel();

            if (visuallyGrounded)
            {
                UpdateWeightsDirectly(1f);

                if (activeWeapon != null &&
                    !activeWeapon.activeSelf &&
                    catAnimator.GetLayerWeight(combatLayerIndex) > 0.4f)
                {
                    activeWeapon.SetActive(true);
                }
            }
            else
            {
                if (weightCoroutine != null)
                {
                    StopCoroutine(weightCoroutine);
                    weightCoroutine = null;
                }

                if (activeWeapon != null && activeWeapon.activeSelf)
                    activeWeapon.SetActive(false);

                if (aimRig != null)
                    aimRig.weight = 0f;

                if (aimConstraint != null)
                    aimConstraint.weight = 0f;

                catAnimator.SetLayerWeight(combatLayerIndex, 0f);

                if (shootingLayerIndex != -1)
                    catAnimator.SetLayerWeight(shootingLayerIndex, 0f);
            }
        }
        else
        {
            UpdateWeightsDirectly(0f);
        }
    }

    private void UpdateWeightsDirectly(float target)
    {
        if (catAnimator == null)
            return;

        if (isReloading)
            return;

        float current = catAnimator.GetLayerWeight(combatLayerIndex);

        float next = Mathf.MoveTowards(
            current,
            target,
            Time.deltaTime / transitionDuration
        );

        catAnimator.SetLayerWeight(combatLayerIndex, next);

        if (shootingLayerIndex != -1)
            catAnimator.SetLayerWeight(shootingLayerIndex, next);

        if (currentSelectedSlot == 1)
        {
            if (aimRig != null)
                aimRig.weight = next;

            if (aimConstraint != null)
                aimConstraint.weight = next;
        }
        else
        {
            if (aimRig != null)
                aimRig.weight = 0f;

            if (aimConstraint != null)
                aimConstraint.weight = 0f;
        }
    }

    private void TryUseItem()
    {
        if (currentSelectedSlot <= 1 || isUsingItem || isReloading)
            return;

        GameObject inHand = currentSelectedSlot == 2 ? weapon2InHand : weapon3InHand;

        if (inHand == null)
            return;

        HealthItem item1 = inHand.GetComponent<HealthItem>();

        if (item1 != null)
        {
            if (catHealth != null &&
                catHealth.currentHealth > 0 &&
                catHealth.currentHealth < catHealth.maxHealth)
            {
                StartCoroutine(UseItemRoutine(item1));
            }

            return;
        }

        HealthItem2 item2 = inHand.GetComponent<HealthItem2>();

        if (item2 != null)
        {
            if (catHealth != null &&
                catHealth.currentHealth > 0 &&
                catHealth.currentHealth < catHealth.maxHealth)
            {
                StartCoroutine(UseItemRoutine2(item2));
            }
        }
    }

    private IEnumerator UseItemRoutine2(HealthItem2 item)
    {
        isUsingItem = true;

        if (statusText != null)
            StartCoroutine(ShowStatusMessage(item.useMessage));

        item.Use(catHealth, catAnimator);

        yield return new WaitForSeconds(1.5f);

        ConsumeItemFromStack();

        if ((currentSelectedSlot == 2 && slot2Count > 0) ||
            (currentSelectedSlot == 3 && slot3Count > 0))
        {
            item.ResetVisuals();
        }

        isUsingItem = false;
    }

    private IEnumerator UseItemRoutine(HealthItem item)
    {
        isUsingItem = true;

        if (statusText != null)
            StartCoroutine(ShowStatusMessage(item.useMessage));

        item.Use(catHealth, catAnimator);

        yield return new WaitForSeconds(1.5f);

        ConsumeItemFromStack();

        if ((currentSelectedSlot == 2 && slot2Count > 0) ||
            (currentSelectedSlot == 3 && slot3Count > 0))
        {
            item.ResetVisuals();
        }

        isUsingItem = false;
    }

    private void ConsumeItemFromStack()
    {
        GameObject itemToDestroy = currentSelectedSlot == 2 ? weapon2InHand : weapon3InHand;

        if (currentSelectedSlot == 2)
        {
            slot2Count--;

            if (slot2Count <= 0)
            {
                isSlot2Full = false;
                slot2WorldPrefab = null;
                slot2HandModelPrefab = null;

                if (slot2Icon != null)
                    slot2Icon.sprite = defaultEmptyIcon;

                weapon2InHand = null;
                DeselectAll();

                if (itemToDestroy != null)
                    Destroy(itemToDestroy);
            }
        }
        else if (currentSelectedSlot == 3)
        {
            slot3Count--;

            if (slot3Count <= 0)
            {
                isSlot3Full = false;
                slot3WorldPrefab = null;
                slot3HandModelPrefab = null;

                if (slot3Icon != null)
                    slot3Icon.sprite = defaultEmptyIcon;

                weapon3InHand = null;
                DeselectAll();

                if (itemToDestroy != null)
                    Destroy(itemToDestroy);
            }
        }

        UpdateUI();
    }

    private IEnumerator ShowStatusMessage(string msg)
    {
        if (statusText == null)
            yield break;

        statusText.gameObject.SetActive(true);

        if (statusText.transform.parent != null)
            statusText.transform.parent.gameObject.SetActive(true);

        statusText.text = msg;

        yield return new WaitForSeconds(1.8f);

        statusText.text = "";
        statusText.gameObject.SetActive(false);

        // НЕ вимикаємо parent, бо parent часто є HUD/Inventory Canvas.
        // Інакше після смерті/рестарту може пропасти весь інвентар.
    }

    private void ToggleSlot(int slotIndex)
    {
        if (isReloading)
            return;

        bool hasItem =
            slotIndex == 1 ||
            slotIndex == 2 && isSlot2Full ||
            slotIndex == 3 && isSlot3Full;

        if (!hasItem)
            return;

        if (currentSelectedSlot == slotIndex)
            DeselectAll();
        else
            SelectSlot(slotIndex);
    }

    private void SelectSlot(int slot)
    {
        currentSelectedSlot = slot;

        UpdateWeaponVisibility();

        if (catAnimator != null)
        {
            float typeValue = slot == 1 ? 0f : 1f;
            catAnimator.SetFloat(weaponTypeParam, typeValue);

            if (slot != 1)
                catAnimator.SetTrigger(equipTriggerParam);
        }

        if (weightCoroutine != null)
            StopCoroutine(weightCoroutine);

        weightCoroutine = StartCoroutine(LerpWeight(1f));

        UpdateUI();
    }

    public int GetCurrentSlot()
    {
        return currentSelectedSlot;
    }

    public bool IsCombatAimActive()
    {
        if (movementScript != null && movementScript.IsJumpLocked)
            return false;

        bool visuallyGrounded = IsVisuallyGroundedForCombat();

        return currentSelectedSlot == 1 && visuallyGrounded;
    }

    public void DeselectAll()
    {
        if (reloadCoroutine != null)
            StopCoroutine(reloadCoroutine);

        isReloading = false;

        if (reloadPrompt != null)
            reloadPrompt.SetActive(false);

        currentSelectedSlot = -1;

        if (weightCoroutine != null)
            StopCoroutine(weightCoroutine);

        weightCoroutine = StartCoroutine(LerpWeight(0f));

        UpdateWeaponVisibility();
        UpdateUI();
    }

    private IEnumerator LerpWeight(float target)
    {
        if (catAnimator == null)
            yield break;

        float start = catAnimator.GetLayerWeight(combatLayerIndex);
        float time = 0f;

        while (time < transitionDuration)
        {
            time += Time.deltaTime;

            float w = Mathf.Lerp(
                start,
                target,
                time / transitionDuration
            );

            catAnimator.SetLayerWeight(combatLayerIndex, w);

            if (shootingLayerIndex != -1)
                catAnimator.SetLayerWeight(shootingLayerIndex, w);

            if (aimRig != null)
                aimRig.weight = currentSelectedSlot == 1 ? w : 0f;

            if (aimConstraint != null)
                aimConstraint.weight = currentSelectedSlot == 1 ? w : 0f;

            yield return null;
        }

        catAnimator.SetLayerWeight(combatLayerIndex, target);

        if (shootingLayerIndex != -1)
            catAnimator.SetLayerWeight(shootingLayerIndex, target);

        if (aimRig != null)
            aimRig.weight = currentSelectedSlot == 1 ? target : 0f;

        if (aimConstraint != null)
            aimConstraint.weight = currentSelectedSlot == 1 ? target : 0f;
    }

    private void UpdateWeaponVisibility()
    {
        if (weapon1InHand != null)
            weapon1InHand.SetActive(currentSelectedSlot == 1);

        if (weapon2InHand != null)
            weapon2InHand.SetActive(currentSelectedSlot == 2);

        if (weapon3InHand != null)
            weapon3InHand.SetActive(currentSelectedSlot == 3);
    }

    private void ClearTemporaryUI()
    {
        if (interactionPromptUI != null)
            interactionPromptUI.SetActive(false);

        if (reloadPrompt != null)
            reloadPrompt.SetActive(false);

        if (statusText != null)
        {
            statusText.text = "";
            statusText.gameObject.SetActive(false);
        }
    }

    private void SetInventoryPanelVisibleForPlayer()
    {
        if (!forceInventoryPanelVisibleAfterRestart)
            return;

        if (GameManager.Instance != null && GameManager.Instance.playerIsInTank)
            return;

        if (droneScript != null && droneScript.isActive)
            return;

        SetActiveWithParents(inventoryPanel, true);
    }

    private void SetActiveWithParents(GameObject target, bool active)
    {
        if (target == null)
            return;

        if (active)
        {
            Transform parent = target.transform.parent;

            while (parent != null)
            {
                if (!parent.gameObject.activeSelf)
                    parent.gameObject.SetActive(true);

                parent = parent.parent;
            }
        }

        target.SetActive(active);
    }

    private void UpdateUI()
    {
        if (slot1Object != null && slot1Object.TryGetComponent<Outline>(out var outline1))
            outline1.enabled = currentSelectedSlot == 1;

        if (rifleIcon != null)
            rifleIcon.color = currentSelectedSlot == 1 ? Color.white : new Color32(45, 45, 45, 110);

        if (slot2Object != null && slot2Object.TryGetComponent<Outline>(out var outline2))
            outline2.enabled = currentSelectedSlot == 2;

        if (slot2Icon != null)
            slot2Icon.color = !isSlot2Full ? new Color(1, 1, 1, 0) : currentSelectedSlot == 2 ? Color.white : new Color32(45, 45, 45, 110);

        if (slot2CountText != null)
            slot2CountText.text = isSlot2Full && slot2Count > 1 ? slot2Count.ToString() : "";

        if (slot3Object != null && slot3Object.TryGetComponent<Outline>(out var outline3))
            outline3.enabled = currentSelectedSlot == 3;

        if (slot3Icon != null)
            slot3Icon.color = !isSlot3Full ? new Color(1, 1, 1, 0) : currentSelectedSlot == 3 ? Color.white : new Color32(45, 45, 45, 110);

        if (slot3CountText != null)
            slot3CountText.text = isSlot3Full && slot3Count > 1 ? slot3Count.ToString() : "";

        if (ammoHUDPanel != null)
            ammoHUDPanel.SetActive(currentSelectedSlot == 1);
    }

    private GameObject GetCurrentWeaponModel()
    {
        if (currentSelectedSlot == 1)
            return weapon1InHand;

        if (currentSelectedSlot == 2)
            return weapon2InHand;

        if (currentSelectedSlot == 3)
            return weapon3InHand;

        return null;
    }

    private void Fire()
    {
        if (currentAmmo <= 0)
        {
            if (!isReloading)
                Reload();

            return;
        }

        currentAmmo--;
        UpdateAmmoUI();

        if (catAnimator != null)
            catAnimator.SetTrigger("Attack");

        if (muzzleFlashPrefab != null && firePoint != null)
        {
            GameObject flash = Instantiate(
                muzzleFlashPrefab,
                firePoint.position,
                firePoint.rotation,
                firePoint
            );

            Destroy(flash, 0.1f);
        }

        RaycastHit hit;
        Vector3 targetPoint;

        if (Physics.Raycast(
            Camera.main.transform.position,
            Camera.main.transform.forward,
            out hit,
            range,
            shootableLayers))
        {
            targetPoint = hit.point;

            if (impactEffect != null)
                Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
        }
        else
        {
            targetPoint =
                Camera.main.transform.position +
                Camera.main.transform.forward * range;
        }

        if (tracerPrefab != null && firePoint != null)
        {
            GameObject tracerObj = Instantiate(
                tracerPrefab,
                firePoint.position,
                firePoint.rotation
            );

            tracerObj.GetComponent<BulletTracer>()?.Init(targetPoint, tracerSpeed);
        }
    }

    private void Reload()
    {
        if (isReloading || reservedAmmo <= 0 || currentAmmo == maxMagSize)
            return;

        reloadCoroutine = StartCoroutine(ReloadSequence());
    }

    private IEnumerator ReloadSequence()
    {
        isReloading = true;

        if (catAnimator != null)
        {
            catAnimator.ResetTrigger("Attack");
            catAnimator.ResetTrigger("Reload");
            catAnimator.SetTrigger("Reload");
            catAnimator.Play("Reload", combatLayerIndex, 0f);
        }

        if (aimRig != null)
            aimRig.weight = 0f;

        if (aimConstraint != null)
            aimConstraint.weight = 0f;

        if (statusText != null)
            StartCoroutine(ShowStatusMessage("RELOADING..."));

        yield return new WaitForSeconds(reloadDuration);

        int needed = maxMagSize - currentAmmo;
        int toAdd = Mathf.Min(needed, reservedAmmo);

        currentAmmo += toAdd;
        reservedAmmo -= toAdd;

        UpdateAmmoUI();

        if (currentSelectedSlot == 1)
        {
            if (aimRig != null)
                aimRig.weight = 1f;

            if (aimConstraint != null)
                aimConstraint.weight = 1f;
        }

        isReloading = false;
    }

    private void UpdateInteractionPrompt()
    {
        if (interactionPromptUI == null || promptText == null)
            return;

        Collider[] hitColliders = Physics.OverlapSphere(
            transform.position,
            interactionRange,
            interactableLayer
        );

        bool found = false;

        foreach (Collider hit in hitColliders)
        {
            InteractableItem item = hit.GetComponentInParent<InteractableItem>();
            AmmoPickup ammo = hit.GetComponentInParent<AmmoPickup>();

            if (item != null)
            {
                promptText.text = "Press [E] to pickup " + item.itemName;
                found = true;
                break;
            }

            if (ammo != null)
            {
                promptText.text = "Press [E] to pickup Ammo";
                found = true;
                break;
            }
        }

        interactionPromptUI.SetActive(found);
    }

    private void TryPickupItem()
    {
        if (isReloading)
            return;

        Collider[] hitColliders = Physics.OverlapSphere(
            transform.position,
            interactionRange,
            interactableLayer
        );

        foreach (Collider hit in hitColliders)
        {
            InteractableItem item = hit.GetComponentInParent<InteractableItem>();
            AmmoPickup ammo = hit.GetComponentInParent<AmmoPickup>();

            if (item != null)
            {
                GameObject sourcePrefab = item.itemPrefab;

                if (sourcePrefab == null)
                    continue;

                if (isSlot2Full && slot2WorldPrefab == sourcePrefab && slot2Count < maxStackSize)
                {
                    slot2Count++;
                    Destroy(item.gameObject);
                    UpdateUI();
                    return;
                }

                if (isSlot3Full && slot3WorldPrefab == sourcePrefab && slot3Count < maxStackSize)
                {
                    slot3Count++;
                    Destroy(item.gameObject);
                    UpdateUI();
                    return;
                }

                if (!isSlot2Full)
                {
                    EquipToSlot(2, item, sourcePrefab);
                    return;
                }

                if (!isSlot3Full)
                {
                    EquipToSlot(3, item, sourcePrefab);
                    return;
                }
            }
            else if (ammo != null)
            {
                AddAmmo(ammo.ammoAmount);
                Destroy(ammo.gameObject);
                return;
            }
        }
    }

    private void EquipToSlot(int slot, InteractableItem item, GameObject sourcePrefab)
    {
        if (mountPoints == null)
            mountPoints = GetComponent<CharacterMountPoints>();

        if (mountPoints == null ||
            mountPoints.rightHandSocket == null ||
            item.handModel == null)
        {
            return;
        }

        GameObject spawnedHandModel = Instantiate(
            item.handModel,
            mountPoints.rightHandSocket
        );

        spawnedHandModel.transform.localPosition = Vector3.zero;
        spawnedHandModel.transform.localRotation = Quaternion.identity;
        spawnedHandModel.SetActive(false);

        if (slot == 2)
        {
            weapon2InHand = spawnedHandModel;
            slot2HandModelPrefab = item.handModel;

            if (slot2Icon != null)
                slot2Icon.sprite = item.itemIcon;

            slot2WorldPrefab = sourcePrefab;
            isSlot2Full = true;
            slot2Count = 1;
            SelectSlot(2);
        }
        else if (slot == 3)
        {
            weapon3InHand = spawnedHandModel;
            slot3HandModelPrefab = item.handModel;

            if (slot3Icon != null)
                slot3Icon.sprite = item.itemIcon;

            slot3WorldPrefab = sourcePrefab;
            isSlot3Full = true;
            slot3Count = 1;
            SelectSlot(3);
        }

        Destroy(item.gameObject);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        UpdateUI();
    }

    private void DropCurrentItem()
    {
        if (currentSelectedSlot <= 1 || isReloading)
            return;

        GameObject prefabToSpawn =
            currentSelectedSlot == 2 ? slot2WorldPrefab : slot3WorldPrefab;

        if (prefabToSpawn == null)
            return;

        Vector3 spawnPos =
            transform.position +
            transform.forward * 1.2f +
            Vector3.up * 0.5f;

        GameObject droppedObj = Instantiate(
            prefabToSpawn,
            spawnPos,
            Quaternion.identity
        );

        InteractableItem interactScript = droppedObj.GetComponent<InteractableItem>();

        if (interactScript != null)
            interactScript.SetSource(prefabToSpawn);

        if (currentSelectedSlot == 2)
        {
            slot2Count--;

            if (slot2Count <= 0)
            {
                if (weapon2InHand != null)
                    Destroy(weapon2InHand);

                isSlot2Full = false;
                slot2WorldPrefab = null;
                slot2HandModelPrefab = null;

                if (slot2Icon != null)
                    slot2Icon.sprite = defaultEmptyIcon;

                DeselectAll();
            }
        }
        else if (currentSelectedSlot == 3)
        {
            slot3Count--;

            if (slot3Count <= 0)
            {
                if (weapon3InHand != null)
                    Destroy(weapon3InHand);

                isSlot3Full = false;
                slot3WorldPrefab = null;
                slot3HandModelPrefab = null;

                if (slot3Icon != null)
                    slot3Icon.sprite = defaultEmptyIcon;

                DeselectAll();
            }
        }

        SetInventoryPanelVisibleForPlayer();

        UpdateUI();
    }

    private void UpdateMovementSpeed(bool isCombat)
    {
        if (movementScript == null)
            return;

        if (!movementScript.IsGrounded && lostGroundTimer > 0f)
            return;

        movementScript.walkSpeed = isCombat ? combatWalkSpeed : normalWalkSpeed;
        movementScript.runSpeed = isCombat ? combatRunSpeed : normalRunSpeed;
    }

    private void UpdateAmmoUI()
    {
        if (currentAmmoText != null)
            currentAmmoText.text = currentAmmo.ToString();

        if (reservedAmmoText != null)
            reservedAmmoText.text = "/ " + reservedAmmo;
    }

    public void AddAmmo(int amount)
    {
        reservedAmmo += amount;
        UpdateAmmoUI();
    }

    public void RemoveCurrentItem()
    {
        if (currentSelectedSlot <= 1)
            return;

        if (currentSelectedSlot == 2)
        {
            slot2Count--;

            if (slot2Count <= 0)
            {
                if (weapon2InHand != null)
                    Destroy(weapon2InHand);

                isSlot2Full = false;
                slot2WorldPrefab = null;
                slot2HandModelPrefab = null;

                if (slot2Icon != null)
                    slot2Icon.sprite = defaultEmptyIcon;

                if (slot2CountText != null)
                    slot2CountText.text = "";

                currentSelectedSlot = -1;
                UpdateWeaponVisibility();

                if (weightCoroutine != null)
                    StopCoroutine(weightCoroutine);

                weightCoroutine = StartCoroutine(LerpWeight(0f));
            }
        }
        else if (currentSelectedSlot == 3)
        {
            slot3Count--;

            if (slot3Count <= 0)
            {
                if (weapon3InHand != null)
                    Destroy(weapon3InHand);

                isSlot3Full = false;
                slot3WorldPrefab = null;
                slot3HandModelPrefab = null;

                if (slot3Icon != null)
                    slot3Icon.sprite = defaultEmptyIcon;

                if (slot3CountText != null)
                    slot3CountText.text = "";

                currentSelectedSlot = -1;
                UpdateWeaponVisibility();

                if (weightCoroutine != null)
                    StopCoroutine(weightCoroutine);

                weightCoroutine = StartCoroutine(LerpWeight(0f));
            }
        }

        UpdateUI();
    }

    // =========================================================
    // PLAYER SAVE DATA
    // =========================================================

    public void CapturePlayerSaveData(PlayerSaveData data)
    {
        if (data == null)
            return;

        data.hasData = true;
        data.currentAmmo = currentAmmo;
        data.reservedAmmo = reservedAmmo;
        data.selectedSlot = currentSelectedSlot;

        data.slot2Full = isSlot2Full;
        data.slot2ItemID = isSlot2Full ? GetSaveIDForWorldPrefab(slot2WorldPrefab) : string.Empty;
        data.slot2Count = isSlot2Full ? slot2Count : 0;

        data.slot3Full = isSlot3Full;
        data.slot3ItemID = isSlot3Full ? GetSaveIDForWorldPrefab(slot3WorldPrefab) : string.Empty;
        data.slot3Count = isSlot3Full ? slot3Count : 0;
    }

    public void ApplyPlayerSaveData(PlayerSaveData data)
    {
        if (data == null || !data.hasData)
            return;

        StopAllCoroutines();
        reloadCoroutine = null;
        weightCoroutine = null;

        isReloading = false;
        isUsingItem = false;
        fireTimer = 0f;

        ClearTemporaryUI();

        currentAmmo = Mathf.Clamp(data.currentAmmo, 0, maxMagSize);
        reservedAmmo = Mathf.Max(0, data.reservedAmmo);

        ClearDynamicInventorySlots();

        if (data.slot2Full)
            RestoreSavedInventorySlot(2, data.slot2ItemID, data.slot2Count);

        if (data.slot3Full)
            RestoreSavedInventorySlot(3, data.slot3ItemID, data.slot3Count);

        currentSelectedSlot = -1;

        if (IsSlotAvailable(data.selectedSlot))
            SelectSlot(data.selectedSlot);
        else
            DeselectAll();

        SetInventoryPanelVisibleForPlayer();

        UpdateWeaponVisibility();
        UpdateAmmoUI();
        UpdateUI();
    }

    public void ResetForNewGame()
    {
        StopAllCoroutines();
        reloadCoroutine = null;
        weightCoroutine = null;

        isReloading = false;
        isUsingItem = false;
        fireTimer = 0f;

        ClearTemporaryUI();

        currentAmmo = defaultCurrentAmmo;
        reservedAmmo = defaultReservedAmmo;

        ClearDynamicInventorySlots();
        DeselectAll();
        SetInventoryPanelVisibleForPlayer();

        UpdateWeaponVisibility();
        UpdateAmmoUI();
        UpdateUI();
    }

    private void ClearDynamicInventorySlots()
    {
        if (weapon2InHand != null)
            Destroy(weapon2InHand);

        if (weapon3InHand != null)
            Destroy(weapon3InHand);

        weapon2InHand = null;
        weapon3InHand = null;

        slot2WorldPrefab = null;
        slot2HandModelPrefab = null;
        isSlot2Full = false;
        slot2Count = 0;

        slot3WorldPrefab = null;
        slot3HandModelPrefab = null;
        isSlot3Full = false;
        slot3Count = 0;

        if (slot2Icon != null)
            slot2Icon.sprite = defaultEmptyIcon;

        if (slot3Icon != null)
            slot3Icon.sprite = defaultEmptyIcon;
    }

    private void RestoreSavedInventorySlot(int slot, string itemID, int count)
    {
        if (string.IsNullOrWhiteSpace(itemID) || count <= 0)
            return;

        SaveableInventoryItem saveableItem = FindSaveableInventoryItem(itemID);

        if (saveableItem == null || saveableItem.worldPrefab == null)
        {
            Debug.LogWarning("[InventoryManager] Cannot restore item. Missing saveable item ID: " + itemID);
            return;
        }

        GameObject handPrefab = GetHandModelPrefab(saveableItem);
        Sprite icon = GetItemIcon(saveableItem);

        if (slot == 2)
        {
            slot2WorldPrefab = saveableItem.worldPrefab;
            slot2HandModelPrefab = handPrefab;
            weapon2InHand = RecreateHandModel(slot2HandModelPrefab);
            isSlot2Full = true;
            slot2Count = Mathf.Clamp(count, 1, maxStackSize);

            if (slot2Icon != null)
                slot2Icon.sprite = icon != null ? icon : defaultEmptyIcon;
        }
        else if (slot == 3)
        {
            slot3WorldPrefab = saveableItem.worldPrefab;
            slot3HandModelPrefab = handPrefab;
            weapon3InHand = RecreateHandModel(slot3HandModelPrefab);
            isSlot3Full = true;
            slot3Count = Mathf.Clamp(count, 1, maxStackSize);

            if (slot3Icon != null)
                slot3Icon.sprite = icon != null ? icon : defaultEmptyIcon;
        }
    }

    private string GetSaveIDForWorldPrefab(GameObject worldPrefab)
    {
        if (worldPrefab == null)
            return string.Empty;

        if (saveableItems == null)
            return worldPrefab.name;

        foreach (SaveableInventoryItem item in saveableItems)
        {
            if (item == null || item.worldPrefab == null)
                continue;

            if (item.worldPrefab == worldPrefab)
                return string.IsNullOrWhiteSpace(item.itemID) ? item.worldPrefab.name : item.itemID;
        }

        Debug.LogWarning("[InventoryManager] World prefab is not in Saveable Items list: " + worldPrefab.name);
        return worldPrefab.name;
    }

    private SaveableInventoryItem FindSaveableInventoryItem(string itemID)
    {
        if (string.IsNullOrWhiteSpace(itemID))
            return null;

        if (saveableItems == null)
            return null;

        foreach (SaveableInventoryItem item in saveableItems)
        {
            if (item == null || item.worldPrefab == null)
                continue;

            string currentID = string.IsNullOrWhiteSpace(item.itemID) ? item.worldPrefab.name : item.itemID;

            if (currentID == itemID)
                return item;
        }

        return null;
    }

    private GameObject GetHandModelPrefab(SaveableInventoryItem item)
    {
        if (item == null)
            return null;

        if (item.handModelPrefab != null)
            return item.handModelPrefab;

        InteractableItem interactable = item.worldPrefab != null
            ? item.worldPrefab.GetComponent<InteractableItem>()
            : null;

        return interactable != null ? interactable.handModel : null;
    }

    private Sprite GetItemIcon(SaveableInventoryItem item)
    {
        if (item == null)
            return null;

        if (item.icon != null)
            return item.icon;

        InteractableItem interactable = item.worldPrefab != null
            ? item.worldPrefab.GetComponent<InteractableItem>()
            : null;

        return interactable != null ? interactable.itemIcon : null;
    }

}
