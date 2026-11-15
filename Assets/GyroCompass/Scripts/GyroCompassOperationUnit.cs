using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GyroCompass
{
    /// <summary>
    /// Front-panel logic of the gyrocompass operation unit.
    /// All visuals are placed in the scene/prefabs; this only wires behaviour and writes LCD text.
    /// </summary>
    public partial class GyroCompassOperationUnit : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private GyroDataSimulator data;

        [Header("Displays")]
        [SerializeField] private LcdPanel headingLcd;
        [SerializeField] private LcdPanel statusLcd;

        [Header("Source buttons")]
        [SerializeField] private PanelButton gyro1Button;
        [SerializeField] private PanelButton gyro2Button;
        [SerializeField] private PanelButton extButton;

        [Header("Dimmer / lamp test")]
        [SerializeField] private PanelButton dimmerDownButton;
        [SerializeField] private PanelButton dimmerUpButton;
        [SerializeField] private float lampTestHoldTime = 0.8f;
        [SerializeField] private float[] dimmerLevels = { 0.12f, 0.32f, 0.55f, 0.78f, 1f };
        [SerializeField] private int dimmerLevel = 4;

        [Header("Control buttons")]
        [SerializeField] private PanelButton failButton;
        [SerializeField] private PanelButton selectButton;
        [SerializeField] private PanelButton upButton;
        [SerializeField] private PanelButton downButton;
        [SerializeField] private PanelButton leftButton;
        [SerializeField] private PanelButton rightButton;
        [SerializeField] private PanelButton commandButton;
        [SerializeField] private PanelButton entButton;

        [Header("Sound")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.15f;
        [SerializeField, Range(0f, 1f)] private float buzzerVolume = 0.35f;

        [Header("Timing")]
        [SerializeField] private Vector2 headingRefresh = new Vector2(0.08f, 0.16f);
        [SerializeField] private float statusRefresh = 0.4f;
        [SerializeField] private float menuTimeout = 40f;

        [Header("Menu")]
        [Tooltip("Selected menu line: visible time, then hidden time (seconds).")]
        [SerializeField] private Vector2 menuBlink = new Vector2(0.55f, 0.3f);

        // Menu = MAIN MENU list, Sub = one of its sub lists, Detail = live data page
        private enum Mode { Boot, Settling, Main, Menu, Sub, Detail }
        private enum Source { Gyro1, Gyro2, Ext }

        private static readonly IFormatProvider Inv = System.Globalization.CultureInfo.InvariantCulture;

        private Mode mode = Mode.Boot;
        private Source source = Source.Gyro1;
        private int mainPage;
        private const int MainPageCount = 4;
        private int menuIndex;
        private int subIndex;
        private float blinkStart;
        private float bootTime;
        private float nextHeadingAt;
        private float nextStatusAt;
        private float lastInputAt;
        private bool alarmActive;
        private bool alarmSilenced;
        private string alarmText = string.Empty;
        private bool lampTestOn;
        private bool dimmerConsumed;
        private AudioClip clickClip, buzzerClip, errorClip;
        private readonly List<PanelButton> allButtons = new List<PanelButton>();

#if ENABLE_INPUT_SYSTEM
        private readonly List<KeyValuePair<Key, PanelButton>> keyMap = new List<KeyValuePair<Key, PanelButton>>();
#endif

        private void Awake()
        {
            foreach (var b in new[] { gyro1Button, gyro2Button, extButton, dimmerDownButton, dimmerUpButton, failButton,
                         selectButton, upButton, downButton, leftButton, rightButton, commandButton, entButton })
            {
                if (b == null) continue;
                allButtons.Add(b);
                b.Pressed += OnAnyPressed;
            }

            Bind(gyro1Button, () => SelectSource(Source.Gyro1));
            Bind(gyro2Button, () => SelectSource(Source.Gyro2));
            Bind(extButton, () => SelectSource(Source.Ext));
            Bind(failButton, OnFailStop);
            Bind(selectButton, OnSelect);
            Bind(upButton, () => OnArrow(-1, 0));
            Bind(downButton, () => OnArrow(1, 0));
            Bind(leftButton, () => OnArrow(0, -1));
            Bind(rightButton, () => OnArrow(0, 1));
            Bind(commandButton, OnCommand);
            Bind(entButton, OnEnter);
            if (dimmerDownButton != null) dimmerDownButton.Released += b => OnDimmerReleased(-1);
            if (dimmerUpButton != null) dimmerUpButton.Released += b => OnDimmerReleased(1);

            if (data != null) data.FaultChanged += OnFaultChanged;

            BuildSounds();
#if ENABLE_INPUT_SYSTEM
            MapKey(Key.Digit1, gyro1Button); MapKey(Key.Digit2, gyro2Button); MapKey(Key.Digit3, extButton);
            MapKey(Key.UpArrow, upButton); MapKey(Key.DownArrow, downButton);
            MapKey(Key.LeftArrow, leftButton); MapKey(Key.RightArrow, rightButton);
            MapKey(Key.Enter, entButton); MapKey(Key.NumpadEnter, entButton);
            MapKey(Key.C, commandButton); MapKey(Key.S, selectButton); MapKey(Key.F, failButton);
            MapKey(Key.Minus, dimmerDownButton); MapKey(Key.Equals, dimmerUpButton);
#endif
        }

        private void Start()
        {
            bootTime = Time.time;
            lastInputAt = Time.time;
            ApplyBrightness();
            SetLampTest(true);
        }

        private void OnDestroy()
        {
            if (data != null) data.FaultChanged -= OnFaultChanged;
        }

        private static void Bind(PanelButton b, Action a)
        {
            if (b != null) b.Clicked += _ => a();
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            ReadKeyboard();
            UpdateLampTestHold();

            float t = Time.time - bootTime;
            switch (mode)
            {
                case Mode.Boot:
                    UpdateBoot(t);
                    break;
                case Mode.Settling:
                    UpdateSettling();
                    break;
                default:
                    if (InMenu && Time.time - lastInputAt > menuTimeout) mode = Mode.Main;
                    if (Time.time >= nextHeadingAt)
                    {
                        nextHeadingAt = Time.time + UnityEngine.Random.Range(headingRefresh.x, headingRefresh.y);
                        RefreshHeading();
                    }
                    if (Time.time >= nextStatusAt)
                    {
                        // menu lists refresh fast so the selected line blinks smoothly
                        nextStatusAt = Time.time + (mode == Mode.Menu || mode == Mode.Sub ? 0.05f : statusRefresh);
                        RefreshStatus();
                    }
                    break;
            }
        }

        private void UpdateBoot(float t)
        {
            if (t < 1.4f) return; // lamp test is showing
            if (lampTestOn && !LampTestHeld()) SetLampTest(false);
            if (headingLcd != null) headingLcd.SetRows(string.Empty, string.Empty, string.Empty);
            if (statusLcd != null)
            {
                statusLcd.SetRows("SELF TEST",
                    t > 1.8f ? "  ROM   OK" : "  ROM  ...",
                    t > 2.3f ? "  RAM   OK" : (t > 1.8f ? "  RAM  ..." : ""),
                    t > 2.8f ? "  COM   OK" : (t > 2.3f ? "  COM  ..." : ""));
            }
            if (t > 3.3f)
            {
                mode = Mode.Settling;
                UpdateSourceLeds();
                if (selectButton != null) selectButton.SetLed(true);
            }
        }

        private void UpdateSettling()
        {
            if (data == null || data.Settled)
            {
                mode = Mode.Main;
                return;
            }
            bool on = Mathf.Repeat(Time.time, 0.8f) < 0.5f;
            string dash = on ? "---.-" : "";
            if (headingLcd != null)
                headingLcd.SetRows(Prefix(Source.Gyro1) + dash,
                    data.Gyro2Installed ? Prefix(Source.Gyro2) + dash : "",
                    Prefix(Source.Ext) + dash);
            if (statusLcd != null)
            {
                int cells = Mathf.RoundToInt(data.SettleProgress * 8f);
                string bar = "[" + new string('⏹', cells) + new string(' ', 8 - cells) + "]";
                statusLcd.SetRows("GYRO", " SETTLING", bar, string.Format(Inv, "   {0,3}%", Mathf.RoundToInt(data.SettleProgress * 100f)));
            }
        }

        // ------------------------------------------------------------------ displays

        private string Prefix(Source s)
        {
            return s == source ? ">" : " ";
        }

        private static string Hdg(float v)
        {
            return Mathf.Repeat((float)Math.Round(v, 1), 360f).ToString("000.0", Inv);
        }

        private void RefreshHeading()
        {
            if (headingLcd == null || data == null) return;
            headingLcd.SetRows(
                Prefix(Source.Gyro1) + Hdg(data.Gyro1),
                data.Gyro2Installed ? Prefix(Source.Gyro2) + Hdg(data.Gyro2) : string.Empty,
                Prefix(Source.Ext) + (data.ExtValid ? Hdg(data.Ext) : "---.-"));
        }

        private void RefreshStatus()
        {
            if (statusLcd == null || data == null) return;

            if (alarmActive && !alarmSilenced)
            {
                bool on = Mathf.Repeat(Time.time, 1f) < 0.6f;
                statusLcd.SetRows(on ? "* ALARM *" : "", alarmText, "", "BZ STOP");
                return;
            }

            if (InMenu) { ShowMenuScreen(); return; }

            switch (mainPage)
            {
                case 0:
                    string spd = data.LogValid ? Signed(Mathf.RoundToInt(data.SpeedKn), "00") : " --";
                    string lat = data.GpsValid
                        ? (data.Latitude >= 0 ? "N" : "S") + " " + Mathf.FloorToInt(Mathf.Abs(data.Latitude)).ToString("00", Inv)
                        : "- --";
                    statusLcd.SetRows("SPD.AUTO", (spd + "[kn]").PadLeft(13), "LAT.AUTO", (lat + "[°]").PadLeft(13));
                    break;
                case 1:
                    statusLcd.SetRows("ROT[°/m]", ("" + Signed(data.RateOfTurn, "00.0")).PadLeft(13),
                        "G1-G2 DIFF", data.Gyro2Installed ? Signed(Mathf.DeltaAngle(data.Gyro2, data.Gyro1), "0.0").PadLeft(13) : "         ----");
                    break;
                case 2:
                    DateTime now = DateTime.UtcNow;
                    statusLcd.SetRows("UTC TIME", "  " + now.ToString("HH:mm:ss", Inv), "DATE", "  " + now.ToString("dd/MM/yy", Inv));
                    break;
                default:
                    if (data.GpsValid)
                        statusLcd.SetRows("POSITION", Dm(data.Latitude, "N", "S", 2), Dm(data.Longitude, "E", "W", 3), "GPS     OK");
                    else
                        statusLcd.SetRows("POSITION", "----.----", "-----.----", "GPS   LOST");
                    break;
            }
        }

        private static string Signed(float v, string fmt)
        {
            return (v >= 0f ? "+" : "-") + Mathf.Abs(v).ToString(fmt, Inv);
        }

        private static string Dm(float deg, string pos, string neg, int degDigits)
        {
            float a = Mathf.Abs(deg);
            int d = Mathf.FloorToInt(a);
            float m = (a - d) * 60f;
            return (deg >= 0 ? pos : neg) + d.ToString(new string('0', degDigits), Inv) + "°" + m.ToString("00.0", Inv) + "'";
        }

        // ------------------------------------------------------------------ buttons

        private bool Ready { get { return mode != Mode.Boot && !lampTestOn; } }

        private void OnAnyPressed(PanelButton b)
        {
            lastInputAt = Time.time;
            Play(clickClip, clickVolume);
        }

        private void SelectSource(Source s)
        {
            if (!Ready) return;
            if (s == Source.Gyro2 && data != null && !data.Gyro2Installed) { Play(errorClip, clickVolume); return; }
            source = s;
            UpdateSourceLeds();
            RefreshHeading();
        }

        private void UpdateSourceLeds()
        {
            if (gyro1Button != null) gyro1Button.SetLed(source == Source.Gyro1);
            if (gyro2Button != null) gyro2Button.SetLed(source == Source.Gyro2);
            if (extButton != null) extButton.SetLed(source == Source.Ext);
        }

        private void OnSelect()
        {
            if (!Ready || mode != Mode.Main) return;
            mainPage = (mainPage + 1) % MainPageCount;
            RefreshStatus();
        }

        private void OnArrow(int vertical, int horizontal)
        {
            if (!Ready) return;
            switch (mode)
            {
                case Mode.Main:
                    int step = vertical != 0 ? vertical : horizontal;
                    mainPage = (mainPage + step + MainPageCount) % MainPageCount;
                    break;
                default:
                    if (InMenu) MenuArrow(vertical, horizontal);
                    return;
            }
            RefreshStatus();
        }

        private void OnFailStop()
        {
            if (!alarmActive) return;
            alarmSilenced = true;
            if (failButton != null) failButton.SetBlink(false);
            if (failButton != null) failButton.SetLed(true);
            StopBuzzer();
            RefreshStatus();
        }

        private void OnFaultChanged(string text, bool active)
        {
            if (active)
            {
                alarmActive = true;
                alarmSilenced = false;
                alarmText = text;
                RecordAlarm(text);
                if (failButton != null) failButton.SetBlink(true, 0.25f);
                StartBuzzer();
            }
            else
            {
                alarmActive = false;
                alarmSilenced = false;
                alarmText = string.Empty;
                if (failButton != null) failButton.SetLed(false);
                StopBuzzer();
            }
            RefreshStatus();
        }

        // ------------------------------------------------------------------ dimmer & lamp test

        private bool LampTestHeld()
        {
            bool down = dimmerDownButton != null && dimmerDownButton.IsHeld;
            bool up = dimmerUpButton != null && dimmerUpButton.IsHeld;
            if (down && up) return true;
            if (down && dimmerDownButton.HeldTime > lampTestHoldTime) return true;
            if (up && dimmerUpButton.HeldTime > lampTestHoldTime) return true;
            return false;
        }

        private void UpdateLampTestHold()
        {
            if (mode == Mode.Boot) return;
            bool held = LampTestHeld();
            if (held) dimmerConsumed = true;
            if (held != lampTestOn) SetLampTest(held);
        }

        private void OnDimmerReleased(int dir)
        {
            bool otherHeld = (dimmerDownButton != null && dimmerDownButton.IsHeld) || (dimmerUpButton != null && dimmerUpButton.IsHeld);
            if (dimmerConsumed)
            {
                if (!otherHeld) dimmerConsumed = false;
                return;
            }
            if (!Ready) return;
            dimmerLevel = Mathf.Clamp(dimmerLevel + dir, 0, dimmerLevels.Length - 1);
            ApplyBrightness();
        }

        private void ApplyBrightness()
        {
            float v = dimmerLevels.Length > 0 ? dimmerLevels[Mathf.Clamp(dimmerLevel, 0, dimmerLevels.Length - 1)] : 1f;
            if (headingLcd != null) headingLcd.SetBrightness(v);
            if (statusLcd != null) statusLcd.SetBrightness(v);
            foreach (var b in allButtons) b.SetBrightness(v);
        }

        private void SetLampTest(bool on)
        {
            lampTestOn = on;
            foreach (var b in allButtons) b.SetLampTest(on);
            if (headingLcd != null) headingLcd.SetLampTest(on);
            if (statusLcd != null) statusLcd.SetLampTest(on);
        }

        // ------------------------------------------------------------------ keyboard

#if ENABLE_INPUT_SYSTEM
        private void MapKey(Key k, PanelButton b)
        {
            if (b != null) keyMap.Add(new KeyValuePair<Key, PanelButton>(k, b));
        }
#endif

        private void ReadKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            foreach (var pair in keyMap)
            {
                var key = kb[pair.Key];
                if (key.wasPressedThisFrame) pair.Value.SimulateDown();
                else if (key.wasReleasedThisFrame) pair.Value.SimulateUp(true);
            }
#endif
        }

        // ------------------------------------------------------------------ sound (generated tones, no UI)

        private void BuildSounds()
        {
            if (audioSource == null) return;
            clickClip = Tone("click", 2600f, 0.025f, 0f, 1);
            errorClip = Tone("error", 600f, 0.12f, 0.04f, 2);
            buzzerClip = Tone("buzzer", 2900f, 0.25f, 0.25f, 1);
        }

        private static AudioClip Tone(string name, float freq, float on, float off, int repeats)
        {
            const int rate = 44100;
            int seg = Mathf.CeilToInt((on + off) * rate);
            int total = seg * repeats;
            var samples = new float[total];
            int onSamples = Mathf.CeilToInt(on * rate);
            for (int i = 0; i < total; i++)
            {
                int local = i % seg;
                if (local >= onSamples) continue;
                float env = Mathf.Clamp01(local / 80f) * Mathf.Clamp01((onSamples - local) / 80f);
                samples[i] = (Mathf.Sin(2f * Mathf.PI * freq * i / rate) > 0f ? 0.6f : -0.6f) * env;
            }
            var clip = AudioClip.Create(name, total, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
        }

        private void StartBuzzer()
        {
            if (audioSource == null || buzzerClip == null) return;
            audioSource.clip = buzzerClip;
            audioSource.loop = true;
            audioSource.volume = buzzerVolume;
            audioSource.Play();
        }

        private void StopBuzzer()
        {
            if (audioSource == null) return;
            audioSource.Stop();
            audioSource.clip = null;
            audioSource.volume = 1f;
        }
    }
}
