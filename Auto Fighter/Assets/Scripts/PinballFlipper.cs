using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PinballFlipper : MonoBehaviour
{
    public HingeJoint hinge;
    public float flipSpeed = 1000f;      // how fast the motor tries to move
    public float returnSpeed = -200f;    // how fast it returns down
    public float maxForce = 10000f;      // motor strength

    void Awake()
    {
        // cache your HingeJoint reference
        hinge = GetComponent<HingeJoint>();
        hinge.useMotor = true;
    }

    public void PaddleMovement(bool isPressed)
    {
        var motor = hinge.motor;
        motor.force = maxForce;
        motor.targetVelocity = isPressed ? flipSpeed : returnSpeed;
        hinge.motor = motor;
    }
}
