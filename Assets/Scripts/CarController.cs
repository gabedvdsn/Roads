using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class CarController : MonoBehaviour
{
    [Header("Car Settings")] 
    [SerializeField] private float accelerationFactor = 30f;
    [SerializeField] float turnFactor = 3.5f;
    [SerializeField] private float driftFactor = .25f;
    [SerializeField] private float dragFactor = 3f;
    [SerializeField] private float maxSpeed = 15f;
    [SerializeField] private float reversePenalty = 0.5f;

    private float accelerationInput = 0;
    private float steeringInput = 0;

    private float rotationAngle = 0;

    private float velocityVsUp = 0;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        ApplyEngineForce();
        RemoveOrthogonalVelocity();
        ApplySteering();
    }

    void ApplyEngineForce()
    {
        velocityVsUp = Vector2.Dot(transform.up, rb.velocity);

        if (velocityVsUp > maxSpeed && accelerationInput > 0) return;
        if (velocityVsUp < -maxSpeed * reversePenalty && accelerationInput < 0) return;

        if (rb.velocity.sqrMagnitude > maxSpeed * maxSpeed && accelerationInput > 0) return;
        
        if (accelerationInput == 0)
        {
            rb.drag = Mathf.Lerp(rb.drag, dragFactor, Time.fixedDeltaTime * dragFactor);
        }
        else rb.drag = 0;
        
        Vector2 engineForce = transform.up * (accelerationInput * accelerationFactor);

        rb.AddForce(engineForce, ForceMode2D.Force);
    }

    void ApplySteering()
    {
        float minSpeedForTurning = rb.velocity.magnitude / 8;
        minSpeedForTurning = Mathf.Clamp01(minSpeedForTurning);
        
        rotationAngle -= steeringInput * turnFactor * minSpeedForTurning;

        rb.MoveRotation(rotationAngle);
    }

    void RemoveOrthogonalVelocity()
    {
        Vector2 forwardVelocity = transform.up * Vector2.Dot(rb.velocity, transform.up);
        Vector2 rightVelocity = transform.right * Vector2.Dot(rb.velocity, transform.right);

        rb.velocity = forwardVelocity + rightVelocity * driftFactor;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        Vector2 input = context.ReadValue<Vector2>();

        steeringInput = input.x;
        accelerationInput = input.y;
    }
}
