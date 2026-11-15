using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GyroCompass
{
    /// <summary>
    /// Fake sensor data. Nothing is read from real hardware: values are produced by a
    /// smoothed random walk so the panel looks like it is receiving live data.
    /// </summary>
    public class GyroDataSimulator : MonoBehaviour
    {
        [Header("Start values")]
        [SerializeField] private bool randomizeStart = true;
        [SerializeField, Range(0f, 359.9f)] private float startHeading = 243.4f;
        [SerializeField] private float startSpeedKn = 7f;
        [SerializeField] private float startLatitude = 27.3f;
        [SerializeField] private float startLongitude = 121.6f;

        [Header("Behaviour")]
        [Tooltip("Seconds until gyros report settled values after power on.")]
        [SerializeField] private float settleTime = 7f;
        [Tooltip("Max rate of turn in degrees per minute.")]
        [SerializeField] private float maxRateOfTurn = 25f;
        [Tooltip("Sea-state yaw amplitude in degrees.")]
        [SerializeField] private float yawAmplitude = 0.35f;
        [SerializeField] private bool gyro2Installed = true;

        [Header("Random faults")]
        [SerializeField] private bool enableFaults = true;
        [SerializeField] private Vector2 faultInterval = new Vector2(70f, 180f);
        [SerializeField] private Vector2 faultDuration = new Vector2(8f, 20f);

        // ---- outputs ----
        public float TrueHeading { get; private set; }
        public float Gyro1 { get; private set; }
        public float Gyro2 { get; private set; }
        public float Ext { get; private set; }
        public float RateOfTurn { get; private set; }   // deg/min
        public float SpeedKn { get; private set; }
        public float Latitude { get; private set; }      // + = N
        public float Longitude { get; private set; }     // + = E
        public float Gyro1Temp { get; private set; }
        public float Gyro2Temp { get; private set; }
        public float AmbientTemp { get; private set; }
        public float AcVolt { get; private set; }
        public float DcVolt { get; private set; }
        public float Gyro1Align { get; private set; }
        public float Gyro2Align { get; private set; }
        public float StepMotorRate { get; private set; } // steps/s
        public float Uptime { get; private set; }

        public bool Gyro2Installed { get { return gyro2Installed; } }
        public bool Settled { get { return Uptime >= settleTime; } }
        public float SettleProgress { get { return settleTime <= 0f ? 1f : Mathf.Clamp01(Uptime / settleTime); } }
        public bool ExtValid { get { return activeFault != Fault.ExtLost; } }
        public bool GpsValid { get { return activeFault != Fault.GpsLost; } }
        public bool LogValid { get { return activeFault != Fault.LogLost; } }
        public string ActiveFaultText { get { return FaultText(activeFault); } }
        public bool HasFault { get { return activeFault != Fault.None; } }

        /// <summary>Raised when a fault starts (true) or clears (false).</summary>
        public event Action<string, bool> FaultChanged;

        private enum Fault { None, ExtLost, GpsLost, LogLost, Gyro2Temp }

        private Fault activeFault = Fault.None;
        private float nextFaultAt;
        private float faultEndsAt;
        private float rotTarget;
        private float nextRotChange;
        private float speedTarget;
        private float nextSpeedChange;
        private float g1Bias, g2Bias, extDeviation;
        private float seed;

        private void Awake()
        {
            seed = Random.value * 1000f;
            TrueHeading = randomizeStart ? Random.Range(0f, 360f) : startHeading;
            SpeedKn = randomizeStart ? Random.Range(5f, 14f) : startSpeedKn;
            speedTarget = SpeedKn;
            Latitude = randomizeStart ? Random.Range(-45f, 45f) : startLatitude;
            Longitude = randomizeStart ? Random.Range(100f, 140f) : startLongitude;
            g1Bias = Random.Range(-0.08f, 0.08f);
            g2Bias = Random.Range(-0.12f, 0.12f);
            extDeviation = Random.Range(-1.4f, 1.4f);
            Gyro1Temp = 38f; Gyro2Temp = 37f; AmbientTemp = Random.Range(22f, 28f);
            AcVolt = 100f; DcVolt = 24f;
            ScheduleFault();
            Step(0f);
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void Step(float dt)
        {
            Uptime += dt;
            float t = Time.time + seed;

            // ---- course keeping: mostly steady, sometimes a turn ----
            if (Time.time >= nextRotChange)
            {
                bool turn = Random.value < 0.35f;
                rotTarget = turn ? Random.Range(-maxRateOfTurn, maxRateOfTurn) : Random.Range(-1.5f, 1.5f);
                nextRotChange = Time.time + (turn ? Random.Range(6f, 15f) : Random.Range(10f, 30f));
            }
            RateOfTurn = Mathf.MoveTowards(RateOfTurn, rotTarget, 6f * dt);
            TrueHeading = Mathf.Repeat(TrueHeading + RateOfTurn / 60f * dt, 360f);

            float yaw = yawAmplitude * Mathf.Sin(t * 0.78f) + yawAmplitude * 0.4f * Mathf.Sin(t * 1.9f + 1.3f);
            float n1 = (Mathf.PerlinNoise(t * 0.9f, 0.1f) - 0.5f) * 0.06f;
            float n2 = (Mathf.PerlinNoise(t * 0.9f, 3.7f) - 0.5f) * 0.08f;
            float n3 = (Mathf.PerlinNoise(t * 2.3f, 7.1f) - 0.5f) * 0.45f;

            Gyro1 = Mathf.Repeat(TrueHeading + yaw + g1Bias + n1, 360f);
            Gyro2 = Mathf.Repeat(TrueHeading + yaw + g2Bias + n2, 360f);
            Ext = Mathf.Repeat(TrueHeading + yaw * 1.3f + extDeviation + n3, 360f);

            // ---- speed log ----
            if (Time.time >= nextSpeedChange)
            {
                speedTarget = Mathf.Clamp(speedTarget + Random.Range(-2f, 2f), 3f, 18f);
                nextSpeedChange = Time.time + Random.Range(20f, 60f);
            }
            float speedNoise = (Mathf.PerlinNoise(t * 0.25f, 11f) - 0.5f) * 0.9f;
            SpeedKn = Mathf.MoveTowards(SpeedKn, speedTarget, 0.05f * dt) + speedNoise * dt;

            // ---- position (real-time dead reckoning, very slow on purpose) ----
            float h = TrueHeading * Mathf.Deg2Rad;
            float nm = SpeedKn * dt / 3600f;
            Latitude += nm * Mathf.Cos(h) / 60f;
            float cosLat = Mathf.Max(0.1f, Mathf.Cos(Latitude * Mathf.Deg2Rad));
            Longitude += nm * Mathf.Sin(h) / (60f * cosLat);

            // ---- housekeeping sensors ----
            float warm = Mathf.Clamp01(Uptime / 120f);
            float g2Extra = activeFault == Fault.Gyro2Temp ? 14f * Mathf.Clamp01((Time.time - (faultEndsAt - 20f)) / 5f) : 0f;
            Gyro1Temp = Mathf.Lerp(38f, 52.4f, warm) + (Mathf.PerlinNoise(t * 0.1f, 21f) - 0.5f) * 0.6f;
            Gyro2Temp = Mathf.Lerp(37f, 51.9f, warm) + (Mathf.PerlinNoise(t * 0.1f, 23f) - 0.5f) * 0.6f + g2Extra;
            AmbientTemp += (Mathf.PerlinNoise(t * 0.05f, 29f) - 0.5f) * 0.02f * dt;
            AcVolt = 100f + (Mathf.PerlinNoise(t * 0.7f, 31f) - 0.5f) * 3.2f;
            DcVolt = 24f + (Mathf.PerlinNoise(t * 0.5f, 37f) - 0.5f) * 0.6f;
            Gyro1Align = g1Bias + n1 * 0.5f;
            Gyro2Align = g2Bias + n2 * 0.5f;
            StepMotorRate = RateOfTurn / 60f / 0.1f; // 1 step = 0.1 deg

            UpdateFaults();
        }

        private void ScheduleFault()
        {
            nextFaultAt = Time.time + Random.Range(faultInterval.x, faultInterval.y);
        }

        private void UpdateFaults()
        {
            if (!enableFaults || !Settled) return;
            if (activeFault == Fault.None && Time.time >= nextFaultAt)
            {
                Fault[] pool = gyro2Installed
                    ? new[] { Fault.ExtLost, Fault.GpsLost, Fault.LogLost, Fault.Gyro2Temp }
                    : new[] { Fault.ExtLost, Fault.GpsLost, Fault.LogLost };
                activeFault = pool[Random.Range(0, pool.Length)];
                faultEndsAt = Time.time + Random.Range(faultDuration.x, faultDuration.y);
                if (activeFault == Fault.Gyro2Temp) faultEndsAt = Time.time + 20f;
                if (FaultChanged != null) FaultChanged(FaultText(activeFault), true);
            }
            else if (activeFault != Fault.None && Time.time >= faultEndsAt)
            {
                string text = FaultText(activeFault);
                activeFault = Fault.None;
                ScheduleFault();
                if (FaultChanged != null) FaultChanged(text, false);
            }
        }

        /// <summary>Force a fault for testing from the inspector context menu.</summary>
        [ContextMenu("Trigger random fault now")]
        public void TriggerFaultNow()
        {
            if (activeFault == Fault.None) nextFaultAt = Time.time;
        }

        private static string FaultText(Fault f)
        {
            switch (f)
            {
                case Fault.ExtLost: return "EXT SIGNAL";
                case Fault.GpsLost: return "GPS LOST";
                case Fault.LogLost: return "LOG LOST";
                case Fault.Gyro2Temp: return "G2 TEMP HI";
                default: return string.Empty;
            }
        }
    }
}
