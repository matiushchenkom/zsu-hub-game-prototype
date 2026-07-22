using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    [Header("Interactions")]
    public DroneInteraction interactionScript;

    [Header("Drone Settings")]
    public float moveForce = 25f;
    public float liftForce = 15f;
    public float rotationSpeed = 90f;
    public float stabilizationSpeed = 5f;

    [Header("Return Home")]
    [Tooltip("Optional. Якщо вказати Home Point, дрон завжди буде повертатися саме сюди.")]
    public Transform homePoint;

    [Tooltip("Якщо Home Point не заданий, дрон запам'ятає свою позицію в сцені.")]
    public bool saveHomeOnStart = true;

    public float returnYOffset = 0f;

    [Header("Camera Control")]
    public Transform cameraTransform;
    public float fixedVerticalAngle = 15f;

    [Header("Visual Tilt")]
    public Transform modelTransform;
    public float tiltAmountForward = 15f;
    public float tiltAmountSideways = 10f;
    public float tiltSpeed = 4f;

    [Header("Propellers")]
    public Transform[] propellers;
    public float propellerSpeed = 1500f;

    [Header("Scanning")]
    public float detectionRadius = 10f;
    public float scanRotationSpeed = 300f;
    public float requiredScanTime = 1f;
    public LayerMask mineLayer;
    public float groundCheckDistance = 0.7f;
    public LayerMask groundLayer;

    [Header("Effects")]
    public ParticleSystem dustParticles;

    private Rigidbody rb;
    private PlayerInput playerInput;
    private Collider[] scanResults = new Collider[50];

    [HideInInspector] public bool isActive = false;

    private bool isTouchingGround = false;
    private float scanTimer = 0f;
    private bool isScanning = false;

    private HashSet<GameObject> discoveredMineRoots = new HashSet<GameObject>();

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool homeSaved = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (saveHomeOnStart)
            SaveCurrentTransformAsHome();

        ResetCameraAngle();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "LoadingScene")
            return;

        discoveredMineRoots.Clear();
        scanTimer = 0f;
        isScanning = false;
    }

    public void Initialize(PlayerInput input)
    {
        playerInput = input;
    }

    public void SaveCurrentTransformAsHome()
    {
        if (homePoint != null)
        {
            homePosition = homePoint.position;
            homeRotation = homePoint.rotation;
        }
        else
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        homeSaved = true;

        Debug.Log("[DroneController] Home saved: " + homePosition);
    }

    public void ActivateDrone(bool value)
    {
        isActive = value;

        if (value)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = true;
        }
        else
        {
            scanTimer = 0f;
            isScanning = false;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;

            if (dustParticles != null)
                dustParticles.Stop();
        }
    }

    private void Update()
    {
        if (!isActive || playerInput == null)
            return;

        isTouchingGround = Physics.Raycast(
            transform.position,
            Vector3.down,
            groundCheckDistance,
            groundLayer
        );

        InputActionMap map = playerInput.currentActionMap;

        InputAction exitAction = map.FindAction("Exit");

        if (exitAction != null && exitAction.WasPressedThisFrame())
        {
            ExitToCat();
            return;
        }

        HandleScanning(map);
        RotatePropellers();
    }

    private void RotatePropellers()
    {
        if (propellers == null)
            return;

        float speed = propellerSpeed * Time.deltaTime;

        foreach (Transform prop in propellers)
        {
            if (prop != null)
                prop.Rotate(Vector3.up * speed);
        }
    }

    private void HandleScanning(InputActionMap map)
    {
        InputAction scanAction = map.FindAction("Scan");

        bool wasScanning = isScanning;
        isScanning = scanAction != null && scanAction.IsPressed() && isTouchingGround;

        if (isScanning)
        {
            scanTimer += Time.deltaTime;

            if (dustParticles != null && !dustParticles.isPlaying)
                dustParticles.Play();

            rb.angularVelocity = Vector3.up * (scanRotationSpeed * Mathf.Deg2Rad);

            if (scanTimer >= requiredScanTime)
            {
                int count = Physics.OverlapSphereNonAlloc(
                    transform.position,
                    detectionRadius,
                    scanResults,
                    mineLayer
                );

                for (int i = 0; i < count; i++)
                {
                    Collider hit = scanResults[i];

                    if (hit == null)
                        continue;

                    GameObject mineRoot = GetMineRoot(hit);

                    if (mineRoot == null)
                        continue;

                    if (discoveredMineRoots.Add(mineRoot))
                        SetMineHighlight(mineRoot, true);
                }
            }
        }
        else
        {
            scanTimer = 0f;

            if (wasScanning)
                rb.angularVelocity = Vector3.zero;

            if (dustParticles != null && dustParticles.isPlaying)
                dustParticles.Stop();
        }
    }

    private GameObject GetMineRoot(Collider hit)
    {
        if (hit == null)
            return null;

        Mine mine = hit.GetComponentInParent<Mine>();

        if (mine != null)
            return mine.gameObject;

        Outline outline = hit.GetComponentInParent<Outline>();

        if (outline != null)
            return outline.gameObject;

        return hit.transform.root.gameObject;
    }

    private void SetMineHighlight(GameObject mineRoot, bool state)
    {
        if (mineRoot == null)
            return;

        MineOutlineActivator activator = mineRoot.GetComponentInChildren<MineOutlineActivator>(true);

        if (activator == null)
        {
            Debug.LogWarning("[DroneController] MineOutlineActivator not found on: " + mineRoot.name, mineRoot);
            return;
        }

        activator.gameObject.SetActive(state);
    }

    private void FixedUpdate()
    {
        if (!isActive || playerInput == null)
            return;

        InputActionMap map = playerInput.currentActionMap;

        float vertical = 0f;

        InputAction liftAction = map.FindAction("Lift");
        InputAction descendAction = map.FindAction("Descend");

        if (liftAction != null && liftAction.IsPressed())
            vertical = liftForce;
        else if (descendAction != null && descendAction.IsPressed())
            vertical = -liftForce;

        rb.AddForce(
            Vector3.up * (Physics.gravity.magnitude + vertical),
            ForceMode.Acceleration
        );

        if (isScanning)
            return;

        InputAction moveAction = map.FindAction("Move");
        Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        rb.AddForce(transform.forward * move.y * moveForce, ForceMode.Acceleration);

        float turn = move.x * rotationSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turn, 0f));

        Quaternion upright = Quaternion.FromToRotation(transform.up, Vector3.up) * rb.rotation;

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                upright,
                Time.fixedDeltaTime * stabilizationSpeed
            )
        );

        ApplyVisualTilt(move);
    }

    private void ApplyVisualTilt(Vector2 input)
    {
        if (modelTransform == null)
            return;

        Quaternion target = Quaternion.Euler(
            input.y * tiltAmountForward,
            0f,
            -input.x * tiltAmountSideways
        );

        modelTransform.localRotation = Quaternion.Lerp(
            modelTransform.localRotation,
            target,
            Time.fixedDeltaTime * tiltSpeed
        );
    }

    public void ReturnToStart()
    {
        if (!homeSaved)
            SaveCurrentTransformAsHome();

        Vector3 targetPosition = homePoint != null ? homePoint.position : homePosition;
        Quaternion targetRotation = homePoint != null ? homePoint.rotation : homeRotation;

        targetPosition += Vector3.up * returnYOffset;

        scanTimer = 0f;
        isScanning = false;

        if (dustParticles != null)
            dustParticles.Stop();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;

        bool oldDetectCollisions = rb.detectCollisions;

        rb.isKinematic = true;
        rb.detectCollisions = false;

        transform.SetPositionAndRotation(targetPosition, targetRotation);
        rb.position = targetPosition;
        rb.rotation = targetRotation;

        Physics.SyncTransforms();

        rb.detectCollisions = oldDetectCollisions;
        rb.isKinematic = true;

        if (modelTransform != null)
            modelTransform.localRotation = Quaternion.identity;

        ResetCameraAngle();

        Debug.Log("[DroneController] Returned to home: " + targetPosition);
    }

    private void ResetCameraAngle()
    {
        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(fixedVerticalAngle, 0f, 0f);
    }

    private void ExitToCat()
    {
        if (interactionScript != null)
            interactionScript.ExitDrone();
    }
}
