using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace GyroCompass
{
    /// <summary>
    /// Physical-looking push button with an optional indicator LED.
    /// Visuals live in the prefab; this script only drives press feedback and LED state.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class PanelButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("References")]
        [SerializeField] private Button button;
        [SerializeField] private RectTransform cap;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image led;
        [SerializeField] private Image ledGlow;

        [Header("LED")]
        [SerializeField] private Color ledOnColor = new Color(1f, 0.78f, 0.22f);
        [SerializeField] private Color ledOffColor = new Color(0.32f, 0.32f, 0.3f);
        [SerializeField] private bool ledOn;

        [Header("Press feedback")]
        [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.95f;

        public event Action<PanelButton> Pressed;
        public event Action<PanelButton> Released;
        public event Action<PanelButton> Clicked;

        public bool IsHeld { get; private set; }
        public float HeldTime { get { return IsHeld ? Time.unscaledTime - downTime : 0f; } }
        public bool HasLed { get { return led != null && led.gameObject.activeSelf; } }
        public bool LedOn { get { return ledOn; } }

        private float downTime;
        private bool blink;
        private float blinkPeriod = 0.5f;
        private bool lampTest;
        private float brightness = 1f;
        private float flashUntil;

        private void Reset()
        {
            button = GetComponent<Button>();
        }

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            button.onClick.AddListener(() => { if (Clicked != null) Clicked(this); });
            ApplyLed();
        }

        private void OnDisable()
        {
            IsHeld = false;
            if (cap != null) cap.localScale = Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!button.interactable) return;
            IsHeld = true;
            downTime = Time.unscaledTime;
            if (cap != null) cap.localScale = Vector3.one * pressedScale;
            if (Pressed != null) Pressed(this);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        private void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            if (cap != null) cap.localScale = Vector3.one;
            if (Released != null) Released(this);
        }

        /// <summary>Visual + logical press from keyboard shortcuts.</summary>
        public void SimulateDown()
        {
            if (IsHeld) return;
            IsHeld = true;
            downTime = Time.unscaledTime;
            if (cap != null) cap.localScale = Vector3.one * pressedScale;
            if (Pressed != null) Pressed(this);
        }

        public void SimulateUp(bool click)
        {
            Release();
            if (click) button.onClick.Invoke();
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        public void SetLed(bool on)
        {
            ledOn = on;
            if (!on) blink = false;
            ApplyLed();
        }

        public void SetBlink(bool on, float period = 0.5f)
        {
            blink = on;
            blinkPeriod = Mathf.Max(0.1f, period);
            if (on) ledOn = true;
            ApplyLed();
        }

        public void Flash(float seconds)
        {
            flashUntil = Time.unscaledTime + seconds;
        }

        public void SetLampTest(bool on)
        {
            lampTest = on;
            ApplyLed();
        }

        public void SetBrightness(float value)
        {
            brightness = Mathf.Clamp01(value);
            ApplyLed();
        }

        private void Update()
        {
            if (blink || flashUntil > 0f) ApplyLed();
        }

        private void ApplyLed()
        {
            if (led == null) return;
            bool flashing = flashUntil > Time.unscaledTime;
            if (!flashing) flashUntil = 0f;
            bool visible = lampTest || flashing || (ledOn && (!blink || Mathf.Repeat(Time.unscaledTime, blinkPeriod * 2f) < blinkPeriod));
            float k = Mathf.Lerp(0.45f, 1f, brightness);
            Color on = new Color(ledOnColor.r * k, ledOnColor.g * k, ledOnColor.b * k, 1f);
            led.color = visible ? on : ledOffColor;
            if (ledGlow != null)
            {
                ledGlow.enabled = visible;
                Color g = ledOnColor;
                g.a = 0.55f * k;
                ledGlow.color = g;
            }
        }
    }
}
