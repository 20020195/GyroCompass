using System;
using System.Collections.Generic;
using UnityEngine;

namespace GyroCompass
{
    /// <summary>
    /// COMMAND menu of the operation unit. The menu text is plain string lists in the Inspector.
    /// MAIN MENU scrolls one line at a time; group items are listed three per page;
    /// the selected line blinks instead of using a cursor.
    /// </summary>
    public partial class GyroCompassOperationUnit
    {
        [Header("Menu (COMMAND)  -  max 14 characters per line")]
        [Tooltip("Lines of MAIN MENU. Line 1 opens Display Items, line 2 Operate Items, line 3 Maintain Items, line 4 Generate Items.")]
        [SerializeField] private List<string> mainMenu = new List<string> { "1 Display", "2 Operate", "3 Maintain", "4 Generate" };
        [Tooltip("One line per item. The number at the start picks the data page (e.g. 10 = heading).")]
        [SerializeField] private List<string> displayItems = new List<string>
            { "10 HDG", "11 DEV", "12 ROT", "13 NMEA Format", "14 Baud Rate", "15 G.Current", "16 BT.Volt", "17 DV.Volt", "19 Soft.Ver." };
        [SerializeField] private List<string> operateItems = new List<string>
            { "20 EXT SEL", "21 SPD SET", "22 LAT SET", "23 Sync.Auto", "24 Sync.Manu", "25 Fol.GAIN", "26 ErrRef.MC", "27 ErrRef.OP", "28 Dev Alrm", "29 W-UP.Time" };
        [SerializeField] private List<string> maintainItems = new List<string>
            { "31 DAC Dummy", "32 Rotate MC", "33 Rotate RC", "34 Reset RT", "35 Reset LOG", "36 Init.MEM", "37 Inst.Err", "39 LOG Ref." };
        [SerializeField] private List<string> generateItems = new List<string>
            { "41 Stepper", "42 Out Port", "43 Out Form", "45 In Form", "49 Misc." };

        private const int LcdColumns = 14;
        private const int VisibleLines = 3;

        private sealed class MenuItem
        {
            public readonly string Label;
            public readonly Func<string[]> Page; // three lines under the title
            public MenuItem(string label, Func<string[]> page) { Label = label; Page = page; }
        }

        private sealed class MenuGroup
        {
            public readonly string Label;   // line shown in MAIN MENU
            public readonly string Title;   // title row of the item list
            public readonly MenuItem[] Items;
            public MenuGroup(string label, string title, MenuItem[] items) { Label = label; Title = title; Items = items; }
        }

        private MenuGroup[] menu;
        private const string MenuTitle = "MAIN MENU";
        private Dictionary<int, Func<string[]>> pages;
        private int mainTop;
        private string lastAlarm = "NONE";
        private DateTime lastAlarmTime;
        private int alarmCount;

        private MenuGroup[] Menu { get { return menu ?? (menu = BuildMenu()); } }
        private bool InMenu { get { return mode == Mode.Menu || mode == Mode.Sub || mode == Mode.Detail; } }
        private MenuGroup Group { get { return Menu[menuIndex]; } }

        // ------------------------------------------------------------------ menu from the string lists

        private MenuGroup[] BuildMenu()
        {
            if (pages == null) pages = BuildPages();
            var lists = new[] { displayItems, operateItems, maintainItems, generateItems };
            var groups = new List<MenuGroup>();
            for (int g = 0; g < mainMenu.Count && g < lists.Length; g++)
            {
                string label = mainMenu[g] ?? string.Empty;
                var items = new List<MenuItem>();
                if (lists[g] != null)
                    foreach (var text in lists[g])
                        if (!string.IsNullOrEmpty(text)) items.Add(new MenuItem(text, PageFor(text)));
                groups.Add(new MenuGroup(label, TitleOf(label), items.ToArray()));
            }
            return groups.ToArray();
        }

        /// <summary>"1 Display" -> "DISPLAY".</summary>
        private static string TitleOf(string label)
        {
            return label.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ', '.').ToUpperInvariant();
        }

        /// <summary>The leading number of an item ("13 NMEA Format" -> 13) chooses its data page.</summary>
        private Func<string[]> PageFor(string text)
        {
            int code = 0, i = 0;
            while (i < text.Length && char.IsDigit(text[i])) code = code * 10 + (text[i++] - '0');
            Func<string[]> f;
            if (pages.TryGetValue(code, out f)) return f;
            return () => L(R("STATE", "OK"), string.Empty, string.Empty); // unknown number: generic page
        }

        // ------------------------------------------------------------------ simulated data pages (key = item number)

        private Dictionary<int, Func<string[]>> BuildPages()
        {
            var d = data;
            Func<float, float, float> noise = (seed, amp) => (Mathf.PerlinNoise(Time.time * 0.4f, seed) - 0.5f) * amp;
            Func<int> hours = () => Mathf.FloorToInt(d.Uptime / 3600f);
            Func<string> lastAt = () => alarmCount > 0 ? lastAlarmTime.ToString("HH:mm", Inv) : "--:--";
            Func<string> lat = () => (d.Latitude >= 0 ? "N " : "S ") + Mathf.FloorToInt(Mathf.Abs(d.Latitude)).ToString("00", Inv) + "°";

            return new Dictionary<int, Func<string[]>>
            {
                // ---- display ----
                { 10, () => L(
                    R("G1", Hdg(d.Gyro1) + "°"),
                    R("G2", d.Gyro2Installed ? Hdg(d.Gyro2) + "°" : "----"),
                    R("EXT", d.ExtValid ? Hdg(d.Ext) + "°" : "---.-")) },
                { 11, () => L(
                    R("EXT DEV", d.ExtValid ? Signed(Mathf.DeltaAngle(d.TrueHeading, d.Ext), "0.0") + "°" : "----"),
                    R("G1-G2", d.Gyro2Installed ? Signed(Mathf.DeltaAngle(d.Gyro2, d.Gyro1), "0.00") + "°" : "----"),
                    R("CORR", "AUTO")) },
                { 12, () => L(
                    R("ROT", Signed(d.RateOfTurn, "00.0") + "°/m"),
                    R("TURN", Mathf.Abs(d.RateOfTurn) < 2f ? "STEADY" : d.RateOfTurn > 0 ? "STBD" : "PORT"),
                    R("FILTER", "MID")) },
                { 13, () => L(R("HDG", "$HEHDT"), R("ROT", "$HEROT"), R("VER", "0183 V4")) },
                { 14, () => L(R("CH1", "4800"), R("CH2", "4800"), R("CH3", "38400")) },
                { 15, () => L(
                    R("G1", (0.42f + noise(41f, 0.03f)).ToString("0.00", Inv) + "A"),
                    R("G2", d.Gyro2Installed ? (0.41f + noise(43f, 0.03f)).ToString("0.00", Inv) + "A" : "----"),
                    R("PHASE", "OK")) },
                { 16, () => L(R("BATT", (d.DcVolt + 3.2f).ToString("00.0", Inv) + "V"), R("CHARGE", "FLOAT"), R("STATE", "OK")) },
                { 17, () => L(
                    R("G1", Signed(d.Gyro1Align * 0.08f, "0.000") + "V"),
                    R("G2", d.Gyro2Installed ? Signed(d.Gyro2Align * 0.08f, "0.000") + "V" : "----"),
                    R("RANGE", "NORMAL")) },
                { 19, () => L(R("MAIN", "2.14"), R("OP UNIT", "1.07"), R("ROM ID", "04271")) },

                // ---- operate ----
                { 20, () => L(
                    R("MASTER", source == Source.Gyro1 ? "GYRO1" : source == Source.Gyro2 ? "GYRO2" : "EXT"),
                    R("EXT", d.ExtValid ? "VALID" : "LOST"),
                    R("AUTO CHG", "ON")) },
                { 21, () => L(R("MODE", "AUTO"), R("SRC", "LOG"),
                    R("SPD", d.LogValid ? Signed(Mathf.RoundToInt(d.SpeedKn), "00") + "kn" : "--kn")) },
                { 22, () => L(R("MODE", "AUTO"), R("SRC", "GPS"), R("LAT", d.GpsValid ? lat() : "----")) },
                { 23, () => L(R("MC", "SYNC OK"), R("REP1-3", "SYNC OK"), R("STATE", "IDLE")) },
                { 24, () => L(R("MC", Hdg(d.Gyro1) + "°"), R("REP", Hdg(d.Gyro1) + "°"), R("STATE", "IDLE")) },
                { 25, () => L(R("GAIN", "3"), R("F/U ERR", Signed(d.Gyro1Align, "0.00") + "°"), R("SPEED", "30°/s")) },
                { 26, () => L(R("MC ALARM", "NONE"), R("SERVO", "OK"), R("ROTOR", "OK")) },
                { 27, () => L(
                    R("OP ALARM", alarmActive ? "YES" : "NONE"),
                    alarmActive ? alarmText : R("COM", "OK"),
                    R("ACK", alarmActive ? (alarmSilenced ? "DONE" : "WAIT") : "-")) },
                { 28, () => L(
                    R("LIMIT", "5.0°"),
                    R("DEV", d.ExtValid ? Mathf.Abs(Mathf.DeltaAngle(d.Gyro1, d.Ext)).ToString("0.0", Inv) + "°" : "----"),
                    R("ALARM", "OFF")) },
                { 29, () => L(R("TIMER", "12h"), R("STATE", "OFF"), R("RANGE", "1-99h")) },

                // ---- maintain ----
                { 31, () => L(
                    R("HDG", (Mathf.PingPong(d.Gyro1, 180f) / 180f * 5f).ToString("0.000", Inv) + "V"),
                    R("ROT", (2.5f + d.RateOfTurn / 30f * 2.344f).ToString("0.000", Inv) + "V"),
                    R("OUTPUT", "OFF")) },
                { 32, () => L(R("MC", Hdg(d.Gyro1) + "°"), R("DRIVE", "STOP"), R("SPEED", "LOW")) },
                { 33, () => L(R("RC1", Hdg(d.Gyro1) + "°"), R("RC2", Hdg(d.Gyro1) + "°"), R("DRIVE", "STOP")) },
                { 34, () => L(
                    R("G1", (12840 + hours()).ToString(Inv) + "h"),
                    R("G2", d.Gyro2Installed ? (9312 + hours()).ToString(Inv) + "h" : "----"),
                    R("RESET", "NO")) },
                { 35, () => L(R("ENTRIES", alarmCount.ToString(Inv)), R("LAST", lastAt()), R("RESET", "NO")) },
                { 36, () => L(R("BACKUP MEM", "OK"), R("CHECKSUM", "OK"), R("INIT", "NO")) },
                { 37, () => L(R("INST ERR", "+0.0°"), R("RANGE", "±180°"), R("STATE", "SET")) },
                { 39, () => L(R("COUNT", alarmCount.ToString(Inv)), lastAlarm, R("AT", lastAt())) },

                // ---- generate ----
                { 41, () => L(R("F/U SPD", "6°/s"), R("REF", "24V"), R("RATE", Signed(d.StepMotorRate, "00") + "/s")) },
                { 42, () => L(R("CH1", "IEC-1"), R("CH2", "IEC-2"), R("CH3", "IEC-1")) },
                { 43, () => L(R("CH1", "HDT ROT"), R("CH2", "HDT"), R("INTERVAL", "200ms")) },
                { 45, () => L(R("EXT", "HDT"), R("RATE", "4800"), R("STATE", d.ExtValid ? "OK" : "LOST")) },
                { 49, () => L(R("BUZZER", "ON"), R("DIM LINK", "ON"), R("LANGUAGE", "ENG")) },
            };
        }

        private float CurrentBrightness
        {
            get { return dimmerLevels.Length > 0 ? dimmerLevels[Mathf.Clamp(dimmerLevel, 0, dimmerLevels.Length - 1)] : 1f; }
        }

        private static string[] L(string a, string b, string c) { return new[] { a, b, c }; }

        /// <summary>Label on the left, value right-aligned to the LCD width.</summary>
        private static string R(string left, string right)
        {
            int pad = LcdColumns - left.Length - right.Length;
            return left + new string(' ', Mathf.Max(1, pad)) + right;
        }

        private void RecordAlarm(string text)
        {
            alarmCount++;
            lastAlarm = text;
            lastAlarmTime = DateTime.UtcNow;
        }

        private static int Wrap(int i, int count)
        {
            return count <= 0 ? 0 : (i % count + count) % count;
        }

        private bool BlinkVisible
        {
            get { return Mathf.Repeat(Time.unscaledTime - blinkStart, menuBlink.x + menuBlink.y) < menuBlink.x; }
        }

        // ------------------------------------------------------------------ rendering

        private void ShowMenuScreen()
        {
            switch (mode)
            {
                case Mode.Menu:
                {
                    // scroll one line at a time ("push up"): 1 2 3 -> 2 3 4
                    if (menuIndex < mainTop) mainTop = menuIndex;
                    if (menuIndex > mainTop + VisibleLines - 1) mainTop = menuIndex - (VisibleLines - 1);
                    var labels = new string[Menu.Length];
                    for (int i = 0; i < labels.Length; i++) labels[i] = Menu[i].Label;
                    ShowList(MenuTitle, labels, menuIndex, mainTop);
                    break;
                }
                case Mode.Sub:
                {
                    // page by three: 10 11 12 / 13 14 15 / 16 17 19
                    var items = Group.Items;
                    var labels = new string[items.Length];
                    for (int i = 0; i < labels.Length; i++) labels[i] = items[i].Label;
                    ShowList(Group.Title, labels, subIndex, subIndex / VisibleLines * VisibleLines);
                    break;
                }
                case Mode.Detail:
                {
                    var item = Group.Items[subIndex];
                    string[] page = item.Page();
                    statusLcd.SetRows(item.Label.ToUpperInvariant(), page[0], page[1], page[2]);
                    break;
                }
            }
        }

        private void ShowList(string title, string[] labels, int selected, int top)
        {
            bool visible = BlinkVisible;
            string[] lines = new string[VisibleLines + 1];
            lines[0] = title;
            for (int i = 0; i < VisibleLines; i++)
            {
                int idx = top + i;
                if (idx >= labels.Length) lines[i + 1] = string.Empty;
                else lines[i + 1] = idx == selected && !visible ? string.Empty : labels[idx];
            }
            statusLcd.SetRows(lines);
        }

        // ------------------------------------------------------------------ navigation

        private void OnCommand()
        {
            if (!Ready) return;
            if (mode == Mode.Main)
            {
                menu = null; // re-read the config so Inspector edits show up without restarting
                if (Menu.Length == 0) return;
                mode = Mode.Menu;
                menuIndex = 0;
                mainTop = 0;
            }
            else if (InMenu) mode = Mode.Main;
            else return;
            if (commandButton != null) commandButton.Flash(0.15f);
            blinkStart = Time.unscaledTime;
            RefreshStatus();
        }

        private void OnEnter()
        {
            if (!Ready || !InMenu) return;
            switch (mode)
            {
                case Mode.Menu:
                    if (Group.Items.Length == 0) return;
                    mode = Mode.Sub;
                    subIndex = 0;
                    break;
                case Mode.Sub:
                    mode = Mode.Detail;
                    break;
                case Mode.Detail:
                    mode = Mode.Sub;
                    break;
            }
            blinkStart = Time.unscaledTime;
            RefreshStatus();
        }

        private void MenuArrow(int vertical, int horizontal)
        {
            switch (mode)
            {
                case Mode.Menu:
                    if (vertical != 0) menuIndex = Wrap(menuIndex + vertical, Menu.Length);
                    else if (horizontal > 0) { OnEnter(); return; }
                    else mode = Mode.Main;
                    break;
                case Mode.Sub:
                    if (vertical != 0) subIndex = Wrap(subIndex + vertical, Group.Items.Length);
                    else if (horizontal > 0) { OnEnter(); return; }
                    else mode = Mode.Menu;
                    break;
                case Mode.Detail:
                    if (vertical != 0) subIndex = Wrap(subIndex + vertical, Group.Items.Length);
                    else if (horizontal < 0) mode = Mode.Sub;
                    break;
            }
            blinkStart = Time.unscaledTime; // newly selected line shows immediately
            RefreshStatus();
        }
    }
}
