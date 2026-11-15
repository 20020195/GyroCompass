using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GyroCompass
{
    /// <summary>
    /// Character LCD (green backlight). Rows are TMP texts placed in the prefab;
    /// each row has a faint "ghost" text that imitates the unlit dot-matrix cells.
    /// </summary>
    public class LcdPanel : MonoBehaviour
    {
        [SerializeField] private Image backlight;
        [SerializeField] private TMP_Text[] rows;
        [SerializeField] private TMP_Text[] ghosts;
        [SerializeField, Min(1)] private int columns = 10;

        [Header("Colors")]
        [SerializeField] private Color backlightBright = new Color(0.69f, 0.86f, 0.33f);
        [SerializeField] private Color backlightDim = new Color(0.22f, 0.30f, 0.10f);
        [SerializeField] private Color textBright = new Color(0.12f, 0.18f, 0.06f);
        [SerializeField] private Color textDim = new Color(0.05f, 0.08f, 0.02f);
        [SerializeField, Range(0f, 0.3f)] private float ghostAlpha = 0.07f;

        private readonly System.Collections.Generic.List<TMP_Text> active = new System.Collections.Generic.List<TMP_Text>();
        private string[] saved;
        private bool lampTest;

        public int RowCount { get { CacheRows(); return active.Count; } }
        public int Columns { get { return columns; } }

        private void Awake()
        {
            CacheRows();
            string ghost = new string('⏹', columns);
            if (ghosts != null)
                foreach (var g in ghosts)
                    if (g != null) g.text = ghost;
            SetBrightness(1f);
        }

        private void CacheRows()
        {
            if (active.Count > 0 || rows == null) return;
            foreach (var r in rows)
                if (r != null && r.gameObject.activeInHierarchy) active.Add(r);
        }

        public void SetRow(int index, string text)
        {
            CacheRows();
            if (lampTest || index < 0 || index >= active.Count) return;
            if (text == null) text = string.Empty;
            if (text.Length > columns) text = text.Substring(0, columns);
            if (active[index].text != text) active[index].text = text;
        }

        public void SetRows(params string[] lines)
        {
            CacheRows();
            for (int i = 0; i < active.Count; i++)
                SetRow(i, lines != null && i < lines.Length ? lines[i] : string.Empty);
        }

        public void SetBrightness(float t)
        {
            t = Mathf.Clamp01(t);
            if (backlight != null) backlight.color = Color.Lerp(backlightDim, backlightBright, t);
            Color tc = Color.Lerp(textDim, textBright, t);
            if (rows != null)
                foreach (var r in rows) if (r != null) r.color = tc;
            if (ghosts != null)
                foreach (var g in ghosts) if (g != null) { Color c = tc; c.a = ghostAlpha; g.color = c; }
        }

        /// <summary>All dots on (lamp test).</summary>
        public void SetLampTest(bool on)
        {
            CacheRows();
            if (on == lampTest) return;
            if (on)
            {
                saved = new string[active.Count];
                string full = new string('⏹', columns);
                for (int i = 0; i < active.Count; i++) { saved[i] = active[i].text; active[i].text = full; }
                lampTest = true;
            }
            else
            {
                lampTest = false;
                if (saved != null)
                    for (int i = 0; i < active.Count && i < saved.Length; i++) active[i].text = saved[i];
            }
        }
    }
}
