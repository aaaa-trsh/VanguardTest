using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum WallrunState {
    None,
    Left,
    Right
}

public enum CrouchState {
    None,
    Sneak,
    Slide,
    Queued
}
public class FirstPersonMove : MonoBehaviour
{
    [Header("Walk Settings")]
    public float speed = 5f;
    public float acceleration = 0.1f;

    private bool isGrounded;
    private Vector3 walkVector;
    private Vector3 immediateWalkVector;
    private RaycastHit groundHit;

    [Header("Jump Settings")]
    public float jumpVelocity = 50f;
    public int maxJumpCount = 2;
    public float airstrafeMultiplier = 0.5f;
    private int jumpCount;

    [Header("Wallrun Settings")]
    public WallrunState wallrunState = WallrunState.None;
    public float wallrunJumpVelocity = 3;
    public float baseWallrunSpeed = 6;
    
    private Vector3 wallNormal;
    private Vector3 wallDirection;
    private float wallrunSpeed;
    private float wallrunTime;
    private bool wallRunEnabled = true;
    private RaycastHit wallHit;

    [Header("Crouch Settings")]
    public CrouchState crouchState = CrouchState.None;
    public float slideBoost = 1.5f;
    private Vector3 slideVel;

    public float sneakSpeed = 0.7f;
    public float eyeHeight = 1;
    public float crouchedEyeHeight = 0.7f; 
    public float minimumSlideVelocity = 2f; 

    private float crouchTime = 0; 

    [Header("Misc")]
    [SerializeField]
    public LayerMask environmentMask;
    private Rigidbody rb;
    private FirstPersonLook camManager;

    private Vector2 targetMoveInputVector;
    private Vector3 moveInputVector;
    public void UpdateMoveInput(InputAction.CallbackContext context) {
        targetMoveInputVector = context.ReadValue<Vector2>();
    }

    private bool inputJump;
    public void UpdateJumpInput(InputAction.CallbackContext context) {
        var newValue = context.ReadValue<float>() == 1;

        if (newValue != inputJump) {
            if (newValue == true && jumpCount < maxJumpCount - 1) {
                
                if (wallrunState != WallrunState.None) {
                    jumpCount = -1; // so fucking jank :D
                    Vector3 fwd = transform.forward * wallrunSpeed;
                    fwd.y = jumpVelocity;
                    rb.velocity = fwd + (wallNormal * wallrunJumpVelocity);

                    ExitWallRun();
                }
                else {
                    Vector3 jumpDirection = crouchState == CrouchState.Slide ? slideVel : rb.velocity;
                    if (Vector3.Distance(targetMoveInputVector, Vector3.zero) > 0.5f && !isGrounded) {
                        jumpDirection = immediateWalkVector.normalized * Vector3.Scale(rb.velocity, new Vector3(1, 0, 1)).magnitude;
                    }
                    jumpDirection.y = jumpVelocity;
                    rb.velocity = jumpDirection;
                }

                jumpCount++;
            }
        }
        inputJump = newValue;
    }

    private bool inputCrouch;
    public void UpdateCrouchInput(InputAction.CallbackContext context) {
        var newValue = context.ReadValue<float>() == 1;
        if (newValue != inputCrouch) {
            if (newValue == true) {
                if (wallrunState != WallrunState.None) {
                    rb.velocity = (wallDirection.normalized * rb.velocity.magnitude) + wallNormal;
                    ExitWallRun();
                }

                if (isGrounded)
                    StartCrouch();
                else
                    crouchState = CrouchState.Queued;
            } else {
                crouchState = CrouchState.None;
            }
        }
        inputCrouch = newValue;
    }

    void Start() {
        rb = GetComponent<Rigidbody>();
        camManager = GetComponent<FirstPersonLook>();
    }

    void FixedUpdate() {

        bool newGrounded = Physics.Raycast(transform.position, Vector3.down, out groundHit, .53f, environmentMask);
        if (isGrounded != newGrounded) {
            if (newGrounded)
                OnGrounded();
            else
                OnExitGround();
        }

        isGrounded = newGrounded;
        WallRunCheck();
        
        Vector3 targetVelocity = rb.velocity;

        moveInputVector = Vector2.Lerp(moveInputVector, targetMoveInputVector, Time.fixedDeltaTime * 1/acceleration);
        walkVector = Vector3.Cross(transform.TransformDirection(new Vector3(moveInputVector.y, 0, -moveInputVector.x)), isGrounded ? groundHit.normal : Vector3.up) * speed;
        immediateWalkVector = Vector3.Cross(transform.TransformDirection(new Vector3(targetMoveInputVector.y, 0, -targetMoveInputVector.x)), isGrounded ? groundHit.normal : Vector3.up) * speed;
        
        if (isGrounded) {
            jumpCount = 0;

            if (crouchState == CrouchState.Sneak || crouchState == CrouchState.Slide) {
                camManager.cam.transform.localPosition = Vector3.up * crouchedEyeHeight;
                if (crouchState == CrouchState.Slide) {
                    slideVel = rb.velocity.normalized * 7;
                    targetVelocity = slideVel;
                } else {
                    targetVelocity = (walkVector / speed) * sneakSpeed;
                }
                crouchTime += Time.fixedDeltaTime;
            }
            else {
                camManager.cam.transform.localPosition = Vector3.up * eyeHeight;
                targetVelocity = walkVector;
            }
            targetVelocity.y = rb.velocity.y;
        }
        else if (wallrunState == WallrunState.None) {
            targetVelocity = (rb.velocity + (immediateWalkVector.normalized * airstrafeMultiplier)).normalized * Mathf.Max(rb.velocity.magnitude, baseWallrunSpeed);
            targetVelocity.y = rb.velocity.y;
        }
        else {
            jumpCount = 0;
            targetVelocity = -(wallNormal * Vector3.Distance(wallHit.point, transform.position)) + (wallDirection * wallrunSpeed * Mathf.Lerp(1.3f, 1, Mathf.Min(wallrunTime, 1)));
            targetVelocity.y = rb.velocity.y / 2;
        }

        //Debug.DrawLine(transform.position, transform.position + wallDirection, Color.red);
        rb.velocity = targetVelocity;
    }

    void OnGrounded() {
        if (crouchState == CrouchState.Queued) {
            StartCrouch();
        }
    }

    void OnExitGround() {
        camManager.cam.transform.localPosition = Vector3.up * eyeHeight;
        if (crouchState != CrouchState.None) {
            crouchState = CrouchState.Queued;
        }
    }

    void StartCrouch() {
        crouchTime = 0;
        float xzMag = Vector3.Scale(rb.velocity, new Vector3(1, 0, 1)).magnitude;
        if (xzMag > minimumSlideVelocity) {
            crouchState = CrouchState.Slide;
        }
        else {
            crouchState = CrouchState.Sneak;
        }
        Debug.Log(crouchState);
    }

    float wallrunCheckLength = 0.6f;
    void WallRunCheck() {
        Vector3 wallrunForwardOffset = transform.forward / 4;

        if (wallRunEnabled) {
            RaycastHit rightHit;
            bool rightCheck = (wallrunState == WallrunState.None && Physics.Raycast(transform.position, transform.right - wallrunForwardOffset, out rightHit, wallrunCheckLength, environmentMask)) ||
                        Physics.Raycast(transform.position, transform.right + wallrunForwardOffset, out rightHit, wallrunCheckLength, environmentMask) ||
                        Physics.Raycast(transform.position, transform.right, out rightHit, wallrunCheckLength, environmentMask);
            
            RaycastHit leftHit;
            bool leftCheck = (wallrunState == WallrunState.None && Physics.Raycast(transform.position, -transform.right - wallrunForwardOffset, out leftHit, wallrunCheckLength, environmentMask)) ||
                        Physics.Raycast(transform.position, -transform.right + wallrunForwardOffset, out leftHit, wallrunCheckLength, environmentMask) ||
                        Physics.Raycast(transform.position, -transform.right, out leftHit, wallrunCheckLength, environmentMask);

            if ((rightCheck || leftCheck) && !isGrounded) {
                if (wallrunState == WallrunState.None) {
                    wallrunSpeed = rb.velocity.magnitude;
                    wallrunTime = 0;
                }

                // TODO: add camera lock to normal
                if (leftCheck && Mathf.Abs(Vector3.Dot(leftHit.normal, Vector3.up)) < 0.1f) {
                    if (wallrunState == WallrunState.None)
                        wallrunState = WallrunState.Left;
                    wallNormal = leftHit.normal;
                    wallDirection = Vector3.Cross(wallNormal, Vector3.up);
                    wallHit = leftHit;
                }
                else if (rightCheck && Mathf.Abs(Vector3.Dot(rightHit.normal, Vector3.up)) < 0.1f) {
                    if (wallrunState == WallrunState.None)
                        wallrunState = WallrunState.Right;
                    wallNormal = rightHit.normal;
                    wallDirection = Vector3.Cross(wallNormal, Vector3.up);
                    wallHit = rightHit;
                }
                camManager.targetDutch = -15 * Vector3.Dot(transform.forward, wallDirection);
                wallrunTime += Time.fixedDeltaTime;
                wallDirection *= Vector3.Dot(transform.forward, wallDirection);
            }
            else if (!rightCheck && !leftCheck) {
                if (wallrunState != WallrunState.None) {
                    wallrunState = WallrunState.None;
                    ExitWallRun();
                }
                wallNormal = Vector3.zero;
                wallDirection = Vector3.zero;
                camManager.targetDutch = 0;
            }
        } 
        else {
            wallrunState = WallrunState.None;
            wallNormal = Vector3.zero;
            wallDirection = Vector3.zero;
            camManager.targetDutch = 0;
        }
    }

    void EnableWallRun() {
        wallRunEnabled = true;
    }
    void ExitWallRun() {
        wallRunEnabled = false;
        Invoke("EnableWallRun", 0.5f);
    }
}
