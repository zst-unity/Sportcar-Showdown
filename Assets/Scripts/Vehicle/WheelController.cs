using System;
using MonoWaves.QoL;
using UnityEngine;

[Serializable]
public class WheelSettings
{
    [Header("Suspension")]
    public float springRestLength = 0.4f;
    public float springTravel = 0.2f;
    public float springStiffness = 30000f;
    public float damperStiffness = 4000f;

    [Header("Wheel")]
    public float wheelRadius = 0.33f;
    public float wheelInertia = 1.5f;
    public float tireRelaxationLength = 1;
    public float slipAnglePeak = 8;

    [ReadOnly] public bool isDriving = false;
    [ReadOnly] public bool isSteering = false;
    [ReadOnly] public bool isLeft = false;

    public WheelSettings Clone()
    {
        return (WheelSettings)MemberwiseClone();
    }
}

public class WheelController : MonoBehaviour
{
    public WheelSettings Settings;

    private Rigidbody _rb;
    private Transform _model;
    private RaycastHit _hit;

    private float _lastLength;
    private float _springLength;
    private float _springVelocity;
    private float _springForce;
    private float _damperForce;

    private Vector3 _suspensionForce;

    public float AngularVelocity { get; private set; }
    private Vector3 _linearVelocity;

    private Vector3 _slip;
    private float _slipAngle;
    private float _slipAngleDynamic;

    private Vector3 _force;

    public void Setup(Rigidbody rb, GameObject model)
    {
        _rb = rb;

        _model = Instantiate(model, transform).transform;
        _model.localScale = new(Settings.isLeft ? -1 : 1, 1, 1);
    }

    public void Step(float torque)
    {
        Acceleration(torque);

        _linearVelocity = _slip = _force = Vector3.zero;

        if (SuspensionForce())
        {
            _linearVelocity = transform.InverseTransformDirection(_rb.GetPointVelocity(_hit.point));

            SlipZ();
            SlipX();

            TireForce();

            Debug.DrawRay(transform.position, _slip.x * transform.right, Color.red);
            Debug.DrawRay(transform.position, _force.y * transform.up / 10000, Color.green);
            Debug.DrawRay(transform.position, _slip.z * transform.forward, Color.blue);
        }

        _model.localPosition = new(0, -_springLength, 0);
        _model.Rotate(AngularVelocity * Mathf.Rad2Deg * Time.fixedDeltaTime, 0, 0);
    }

    // TODO: wheel friction
    private void Acceleration(float torque)
    {
        float totalTorque = torque - _force.z * Settings.wheelRadius;
        float angularAcceleration = totalTorque / Settings.wheelInertia;

        AngularVelocity += angularAcceleration * Time.fixedDeltaTime;
    }

    // TODO: apply force at hit.point or transform.position
    private bool SuspensionForce()
    {
        if (Physics.Raycast(transform.position, -transform.up, out _hit, Settings.springRestLength + Settings.springTravel + Settings.wheelRadius))
        {
            _lastLength = _springLength;
            _springLength = (transform.position - (_hit.point + (transform.up * Settings.wheelRadius))).magnitude;
            _springVelocity = (_lastLength - _springLength) / Time.fixedDeltaTime;

            _springForce = (Settings.springRestLength - _springLength) * Settings.springStiffness;
            _damperForce = Settings.damperStiffness * _springVelocity;

            _force.y = _springForce + _damperForce;
            _suspensionForce = _force.y * _hit.normal.normalized;

            _rb.AddForceAtPosition(_suspensionForce, transform.position);

            Vector3 start = transform.position - transform.up * (_springLength);

            Debug.DrawRay(start, -transform.up * Settings.wheelRadius, Color.magenta);
            return true;
        }
        else
        {
            _springLength = Settings.springRestLength + Settings.springTravel;
            return false;
        }
    }

    private void SlipZ()
    {
        float targetAngularVelocity = _linearVelocity.z / Settings.wheelRadius;
        float targetAngularAcceleration = (AngularVelocity - targetAngularVelocity) / Time.fixedDeltaTime;
        float targetFrictionTorque = targetAngularAcceleration * Settings.wheelInertia;
        float maxFrictionTorque = _force.y * Settings.wheelRadius;

        if (_force.y == 0)
            _slip.z = 0;
        else
            _slip.z = Mathf.Clamp(targetFrictionTorque / maxFrictionTorque, -1, 1); // TODO: remove clamp
    }

    private void SlipX()
    {
        if (_linearVelocity.z != 0)
        {
            _slipAngle = Mathf.Rad2Deg * Mathf.Atan(-_linearVelocity.x / Mathf.Abs(_linearVelocity.z));
        }
        else
        {
            _slipAngle = 0;
        }

        float slipAngleLerp = Mathf.Lerp(Settings.slipAnglePeak * Mathf.Sign(-_linearVelocity.x), _slipAngle,
            _linearVelocity.magnitude.Map(3, 6, 0, 1)
        );

        float coeff = Mathf.Abs(_linearVelocity.x) / Settings.tireRelaxationLength/* * Time.fixedDeltaTime*/; // TODO: че это

        _slipAngleDynamic = Mathf.Clamp(
            _slipAngleDynamic + (slipAngleLerp - _slipAngleDynamic) * Mathf.Clamp(coeff, 0, 1),
            -90, 90
        );

        _slip.x = Mathf.Clamp(_slipAngleDynamic / Settings.slipAnglePeak, -1, 1); // TODO: remove clamp
    }

    private void TireForce()
    {
        _force.x = Mathf.Max(_force.y, 0) * _slip.x;
        _force.z = Mathf.Max(_force.y, 0) * _slip.z;

        Vector3 force =
            Vector3.ProjectOnPlane(transform.right, _hit.normal).normalized * _force.x +
            Vector3.ProjectOnPlane(transform.forward, _hit.normal).normalized * _force.z;

        _rb.AddForceAtPosition(force, _hit.point);
    }
}
