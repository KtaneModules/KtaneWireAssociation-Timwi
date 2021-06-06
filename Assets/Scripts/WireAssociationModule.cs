using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WireAssociation;
using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Wire Association
/// Created by Timwi
/// </summary>
public class WireAssociationModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;

    public MeshFilter WireTemplate;
    public TextMesh TextTemplate;
    public KMSelectable ButtonTemplate;
    public KMSelectable SubmitButton;
    public Transform Inside;
    public Material LitLedMaterial;
    public Material UnlitLedMaterial;
    public Transform Shelf;
    public TextMesh DisplayText;
    public MeshRenderer LidTop;
    public MeshRenderer LidBottom;
    public Mesh Quad;

    public Transform WireParent;
    public GameObject Dummy;

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    private readonly List<List<int>> _topGroups = new List<List<int>>();
    private readonly List<List<int>> _bottomGroups = new List<List<int>>();
    private int[] _assoc;
    private int? _selected;
    private int _stage;
    private int _numWires;
    private bool _isSolved;
    private int _expect = 0;
    private int _animating = 0;
    private int _baseSeed = 0;

    private MeshFilter[] _wires;
    private MeshFilter[] _wireHighlights;
    private MeshFilter[] _wireCoppers;
    private MeshCollider[] _wireColliders;
    private KMSelectable[] _wireSels;
    private MeshRenderer[] _leds;
    private KMSelectable[] _buttons;
    private TextMesh[] _texts;
    private Mesh _quadTop;
    private Mesh _quadBottom;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _baseSeed = Rnd.Range(0, int.MaxValue);

        _numWires = Rnd.Range(11, 17);
        Debug.LogFormat("[Wire Association #{0}] Number of wires: {1}", _moduleId, _numWires);

        _quadTop = Instantiate(Quad);
        _quadBottom = Instantiate(Quad);
        LidTop.GetComponent<MeshFilter>().sharedMesh = _quadTop;
        LidBottom.GetComponent<MeshFilter>().sharedMesh = _quadBottom;

        _wires = new MeshFilter[_numWires];
        _wireSels = new KMSelectable[_numWires];
        _wireHighlights = new MeshFilter[_numWires];
        _wireCoppers = new MeshFilter[_numWires];
        _wireColliders = new MeshCollider[_numWires];
        _leds = new MeshRenderer[_numWires];
        _texts = new TextMesh[_numWires];
        _buttons = new KMSelectable[_numWires];

        for (var i = 0; i < _numWires; i++)
        {
            _wires[i] = i == 0 ? WireTemplate : Instantiate(WireTemplate, WireParent);
            _wires[i].name = string.Format("Wire {0}", i);
            _wires[i].transform.localPosition = new Vector3(0, 0, 0);

            _wireHighlights[i] = _wires[i].transform.Find("Highlight").GetComponent<MeshFilter>();
            _wireCoppers[i] = _wires[i].transform.Find("Copper").GetComponent<MeshFilter>();
            _wireColliders[i] = _wires[i].GetComponent<MeshCollider>();

            _wireSels[i] = _wires[i].GetComponent<KMSelectable>();
            _wireSels[i].OnInteract = WirePressed(i);

            _texts[i] = i == 0 ? TextTemplate : Instantiate(TextTemplate, Inside);
            _texts[i].gameObject.name = string.Format("Text {0}", i);

            _buttons[i] = i == 0 ? ButtonTemplate : Instantiate(ButtonTemplate, Inside);
            _buttons[i].gameObject.name = string.Format("Button {0}", i);
            _buttons[i].OnInteract = ButtonPressed(i);
            _leds[i] = _buttons[i].transform.Find("Led").GetComponent<MeshRenderer>();
        }

        SubmitButton.OnInteract = Submit;
        Destroy(Dummy);
        StartCoroutine(Setup(0, firstRun: true));
    }

    private KMSelectable.OnInteractHandler ButtonPressed(int btn)
    {
        return delegate
        {
            _buttons[btn].AddInteractionPunch(.2f);
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, _buttons[btn].transform);
            Audio.PlaySoundAtTransform("Beep", Inside);
            if (_isSolved || _animating > 0)
                return false;

            var grs = _stage == 1
                ? _bottomGroups.Select(g => g.Select(w => _assoc[w]).ToList()).ToList()
                : _topGroups.Select(g => g.Select(w => Array.IndexOf(_assoc, w)).ToList()).ToList();
            var gr = grs.First(g => g.Contains(btn));
            for (var i = 0; i < _numWires; i++)
                _leds[i].sharedMaterial = gr.Contains(i) ? LitLedMaterial : UnlitLedMaterial;
            return false;
        };
    }

    private bool Submit()
    {
        SubmitButton.AddInteractionPunch(.2f);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, SubmitButton.transform);
        if (_animating == 0 && !_isSolved && _stage < 2)
            StartCoroutine(Setup(_stage + 1));
        return false;
    }

    private IEnumerator Setup(int newStage, bool firstRun = false)
    {
        DisplayText.text = "";
        _animating++;
        if (newStage == 0)
        {
            _topGroups.Clear();
            _bottomGroups.Clear();
            for (var i = 0; i < _numWires; i++)
            {
                _topGroups.Add(new List<int> { i });
                _bottomGroups.Add(new List<int> { i });
            }

            _expect = 0;
            _assoc = Enumerable.Range(0, _numWires).ToArray().Shuffle();
            Debug.LogFormat("[Wire Association #{0}] Wires are: {1}", _moduleId, Enumerable.Range(0, _numWires).Select(w => string.Format("{0}={1}", (char) ('A' + w), Array.IndexOf(_assoc, w) + 1)).Join(", "));
        }

        if (!firstRun)
            foreach (var obj in openCloseAnimation(true))
                yield return obj;

        _stage = newStage;
        GenerateWires(0);

        var children = new KMSelectable[3 * _numWires];
        children[_numWires / 2] = SubmitButton;
        Shelf.localEulerAngles = new Vector3(0, _stage == 1 ? 0 : 180, 0);

        for (var i = 0; i < _numWires; i++)
        {
            var x = (float) GetX(i);

            children[i + (_stage == 1 ? 1 : 2) * _numWires] = _wireSels[i];
            children[i + (_stage == 1 ? 2 : 1) * _numWires] = _stage > 0 ? _buttons[i] : null;

            _texts[i].transform.localPosition = new Vector3(x, .0101f, _stage == 1 ? (i % 2 == 0 ? .0015f : -.0135f) : (i % 2 == 0 ? 0 : .014f));
            _texts[i].text = _stage == 1 ? ((char) ('A' + i)).ToString() : (i + 1).ToString();
            _texts[i].anchor = _stage == 1 ? TextAnchor.UpperCenter : TextAnchor.LowerCenter;

            _buttons[i].transform.localPosition = new Vector3(x, .0141f, (i % 2 == 0 ? .036f : .047f) * (_stage == 1 ? -1 : 1));
            _buttons[i].gameObject.SetActive(_stage > 0);
        }

        yield return null;
        setChildren(children, _numWires);
        _selected = null;
        for (var i = 0; i < _numWires; i++)
            _leds[i].sharedMaterial = UnlitLedMaterial;

        foreach (var obj in openCloseAnimation(false, firstRun))
            yield return obj;
        _animating--;
        DisplayText.text = _stage == 2 ? "A" : "—";
    }

    private void setChildren(KMSelectable[] children, int childRowLength)
    {
        var mainSelectable = Module.GetComponent<KMSelectable>();
        mainSelectable.Children = children;
        mainSelectable.ChildRowLength = childRowLength;
        mainSelectable.UpdateChildren();
    }

    private IEnumerator shelfAnimation(bool close)
    {
        _animating++;
        var elapsed = 0f;
        var duration = .9f;

        while (elapsed < duration)
        {
            Inside.localPosition = new Vector3(0, Easing.InOutQuad(elapsed, close ? 0 : -.0059f, close ? -.0059f : 0, duration), -.02f);
            GenerateWires(Easing.InOutQuad(elapsed, 20, 0, duration));
            yield return null;
            elapsed += Time.deltaTime;
        }

        Inside.localPosition = new Vector3(0, close ? -.0059f : 0, -.02f);
        _animating--;
    }
    private IEnumerator lidAnimation(bool close)
    {
        _animating++;
        var elapsed = 0f;
        var duration = .9f;

        var topPosOpen = new Vector3(0, .011f, .0375f);
        var topPosClose = new Vector3(0, .011f, .01f);
        var bottomPosOpen = new Vector3(0, .011f, -.0775f);
        var bottomPosClose = new Vector3(0, .011f, -.05f);

        var scaleOpen = new Vector3(.16f, .005f, .1f);
        var scaleClose = new Vector3(.16f, .06f, .1f);

        while (elapsed < duration)
        {
            var t = Easing.InOutQuad(elapsed, 0, 1, duration);
            LidTop.transform.localPosition = Vector3.Lerp(close ? topPosOpen : topPosClose, close ? topPosClose : topPosOpen, t);
            LidBottom.transform.localPosition = Vector3.Lerp(close ? bottomPosOpen : bottomPosClose, close ? bottomPosClose : bottomPosOpen, t);
            LidTop.transform.localScale = LidBottom.transform.localScale = Vector3.Lerp(close ? scaleOpen : scaleClose, close ? scaleClose : scaleOpen, t);

            var y = LidTop.transform.localScale.y / .06f;
            _quadTop.uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, y), new Vector2(1, 0), new Vector2(0, y) };
            _quadBottom.uv = new Vector2[] { new Vector2(0, 1 - y), new Vector2(1, 1), new Vector2(1, 1 - y), new Vector2(0, 1) };

            yield return null;
            elapsed += Time.deltaTime;
        }
        LidTop.transform.localPosition = close ? topPosClose : topPosOpen;
        LidBottom.transform.localPosition = close ? bottomPosClose : bottomPosOpen;
        LidTop.transform.localScale = LidBottom.transform.localScale = close ? scaleClose : scaleOpen;
        if (close)
            yield return new WaitForSeconds(.8f);
        _animating--;
    }

    private IEnumerable<object> openCloseAnimation(bool close, bool noSound = false)
    {
        if (!noSound)
            Audio.PlaySoundAtTransform(close ? "Close" : "Open", Inside);
        StartCoroutine(close ? shelfAnimation(close) : lidAnimation(close));
        yield return new WaitForSeconds(.4f);
        StartCoroutine(close ? lidAnimation(close) : shelfAnimation(close));
        while (_animating > 1)
            yield return null;
    }

    private double GetX(double wireIx)
    {
        const double width = .14;
        return wireIx * width / (_numWires - 1) - width / 2;
    }

    private KMSelectable.OnInteractHandler WirePressed(int wire)
    {
        return delegate
        {
            _wireSels[wire].AddInteractionPunch(.2f);
            if (_isSolved || _animating > 0)
                return false;

            if (_stage == 2)
            {
                if (_assoc[wire] == _expect)
                {
                    Audio.PlaySoundAtTransform("Beep2", Inside);
                    Debug.LogFormat("[Wire Association #{0}] {1}={2} is correct.", _moduleId, (char) ('A' + _expect), wire + 1);
                    _expect++;
                    DisplayText.text = ((char) ('A' + _expect)).ToString();
                    if (_expect == _numWires)
                    {
                        Debug.LogFormat("[Wire Association #{0}] Module solved.", _moduleId);
                        DisplayText.text = "+";
                        _isSolved = true;
                        StartCoroutine(Victory());
                        StartCoroutine(openCloseAnimation(true).GetEnumerator());
                        setChildren(new[] { SubmitButton }, 1);
                    }
                }
                else
                {
                    Debug.LogFormat("[Wire Association #{0}] You entered {1}={2}. Strike!", _moduleId, (char) ('A' + _expect), wire + 1);
                    Module.HandleStrike();
                    DisplayText.text = "×";
                    StartCoroutine(Setup(0));
                }
            }
            else if (_selected == null)
            {
                Debug.LogFormat("<Wire Association #{0}> Selected wire {1}", _moduleId, wire);
                _selected = wire;
            }
            else
            {
                var grs = _stage == 0 ? _bottomGroups : _topGroups;
                var groupIx = grs.IndexOf(gr => gr.Contains(wire));
                var group = grs[groupIx];
                var otherGroupIx = grs.IndexOf(gr => gr.Contains(_selected.Value));
                if (otherGroupIx == groupIx)
                {
                    grs.RemoveAt(groupIx);
                    grs.AddRange(group.Select(w => new List<int> { w }));
                }
                else
                {
                    grs[otherGroupIx].AddRange(group);
                    grs[otherGroupIx].Sort();
                    grs.RemoveAt(groupIx);
                }
                grs.Sort((a, b) => a[0].CompareTo(b[0]));
                Debug.LogFormat("<Wire Association #{0}> Groups are: {1}", _moduleId, grs.Select(gr => string.Format("[{0}]", gr.Join(", "))).Join("; "));
                _selected = null;
                GenerateWires(20);
            }
            return false;
        };
    }

    private IEnumerator Victory()
    {
        yield return new WaitForSeconds(1.5f);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
        Module.HandlePass();
    }

    private void GenerateWires(double angleMultiplier)
    {
        var groupGroups = new List<List<List<int>>>();

        if (_stage < 2)
        {
            var grs = (_stage == 1 ? _topGroups : _bottomGroups).OrderBy(g => g.Min()).ToList();
            while (grs.Count > 0)
            {
                var grgr = new List<List<int>> { grs[0] };
                grs.RemoveAt(0);
                while (true)
                {
                    var f = grs.IndexOf(gr => gr.Min() > grgr.Max(gg => gg.Max()));
                    if (f == -1)
                        break;
                    grgr.Add(grs[f]);
                    grs.RemoveAt(f);
                }
                groupGroups.Add(grgr);
            }
        }

        for (var i = 0; i < _numWires; i++)
        {
            var gr = _stage == 2 ? null : (_stage == 1 ? _topGroups : _bottomGroups).First(g => g.Contains(i));
            var midPoint = _stage == 2 ? 0 : (gr.Min() + gr.Max()) * .5;
            var angle = _stage == 2 ? 0 : angleMultiplier * groupGroups.IndexOf(gg => gg.Any(g => g.Contains(i)));
            var meshes = WireMeshGenerator.GenerateWire(GetX(i), GetX(_stage == 2 ? i : midPoint), angle, _stage == 1, false, _baseSeed ^ i);

            _wires[i].sharedMesh = meshes.Wire;
            _wireCoppers[i].sharedMesh = meshes.Copper;
            _wireColliders[i].sharedMesh = meshes.Highlight;
            _wireHighlights[i].sharedMesh = meshes.Highlight;

            MeshFilter mf;
            for (var j = 0; j < _wireHighlights[i].transform.childCount; j++)
                if ((mf = _wireHighlights[i].transform.GetChild(j).GetComponent<MeshFilter>()) != null)
                    mf.sharedMesh = meshes.Highlight;
        }
    }
}
