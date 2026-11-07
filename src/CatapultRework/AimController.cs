using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Local aiming session. Added to the catapult when the local player enters aim mode and
    /// destroyed when they leave. Uses the same "doodad control" mechanism as a ship's helm,
    /// so the player is held in place and their controls are routed here.
    /// </summary>
    public class AimController : MonoBehaviour, IDoodadController
    {
        public static AimController Active { get; private set; }

        private Catapult _catapult;
        private CatapultNet _net;
        private Player _player;

        // Marker in polar coordinates around the launch point: yaw offset from the front, horizontal distance.
        private float _yawDeg;
        private float _dist;
        private float _camHeight;

        private Vector3 _launch;
        private Vector3 _front;
        private Vector3 _target;
        private float _minR, _maxR, _absMaxR;
        private bool _targetValid;
        private float _flightTime;
        private Ballistics.Params _params;

        private Vector2 _mouseAccum;
        private Vector3 _moveInput;
        private bool _fireRequested;
        private float _lastHealth;
        private float _nextHeartbeat;
        private float _fireCooldownUntil;

        private bool _camInit;
        private Vector3 _camPos;
        private Quaternion _camRot;

        private LineRenderer _sectorLine, _spreadLine, _markerLine, _pathLine;
        private static Material s_lineMat;

        private readonly float[] _xs = new float[256];
        private readonly float[] _ys = new float[256];

        // ---------------------------------------------------------------- entry / exit

        public static bool TryStart(Catapult c, Player player)
        {
            if (player == null || player != Player.m_localPlayer) return false;
            if (!ModConfig.Enabled.Value || !CatapultMath.LegsReady(c)) return false;
            if (Active != null) return false;
            if (player.GetDoodadController() != null || player.IsEncumbered() || player.InPlaceMode()) return false;

            CatapultNet net = c.GetComponent<CatapultNet>();
            if (net == null) return false;
            if (net.IsAimedByOther(player.GetPlayerID()))
            {
                player.Message(MessageHud.MessageType.Center, "Someone else is aiming this catapult");
                return false;
            }

            AimController ctrl = c.gameObject.AddComponent<AimController>();
            ctrl.Setup(c, net, player);
            Active = ctrl;
            player.StartDoodadControl(ctrl);
            return true;
        }

        private void Setup(Catapult c, CatapultNet net, Player player)
        {
            _catapult = c;
            _net = net;
            _player = player;
            _camHeight = ModConfig.CameraHeight.Value;
            _lastHealth = player.GetHealth();
            RecomputeGeometry();
            _yawDeg = 0f;
            _dist = Mathf.Lerp(_minR, _maxR, 0.5f);
            _net.SendAimer(player.GetPlayerID());
            _nextHeartbeat = Time.time + CatapultNet.AimerHeartbeat;
            CreateLines();
        }

        private bool _stopped;

        public void Stop()
        {
            // StopDoodadControl only calls OnUseStop while IsValid() is true, so always clean up afterwards.
            if (_player != null && _player.GetDoodadController() == (IDoodadController)this)
                _player.StopDoodadControl();
            Cleanup();
        }

        private void Cleanup()
        {
            if (_stopped) return;
            _stopped = true;
            if (Active == this) Active = null;
            if (_net != null) _net.SendAimer(0L);
            DestroyLines();
            if (this != null) Destroy(this);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            DestroyLines();
        }

        // ---------------------------------------------------------------- IDoodadController

        public void OnUseStop(Player player) => Cleanup();

        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block) { }

        public Component GetControlledComponent() => _catapult;

        public Vector3 GetPosition() =>
            _catapult != null ? _catapult.m_loadPoint.transform.position : transform.position;

        public bool IsValid() =>
            this != null && _catapult != null && ModConfig.Enabled.Value && CatapultMath.LegsReady(_catapult);

        // ---------------------------------------------------------------- input from patches

        internal void AddMouse(Vector2 delta) => _mouseAccum += delta;
        internal void SetMove(Vector3 move) => _moveInput = move;
        internal void RequestFire() => _fireRequested = true;

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (_player != null && _player.GetDoodadController() != (IDoodadController)this)
            {
                Cleanup(); // control was released by the game without telling us
                return;
            }
            if (!IsValid() || _player == null || _player.IsDead())
            {
                Stop();
                return;
            }

            if (ModConfig.ExitOnDamage.Value && _player.GetHealth() < _lastHealth - 0.01f)
            {
                Stop();
                return;
            }
            _lastHealth = _player.GetHealth();

            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                Stop();
                return;
            }

            if (Time.time >= _nextHeartbeat)
            {
                _net.SendAimer(_player.GetPlayerID());
                _nextHeartbeat = Time.time + CatapultNet.AimerHeartbeat;
            }

            RecomputeGeometry();
            UpdateMarker(Time.deltaTime);
            UpdateTarget();

            if (_fireRequested)
            {
                _fireRequested = false;
                TryFire();
            }

            UpdateLines();
        }

        private void RecomputeGeometry()
        {
            _front = CatapultMath.Front(_catapult);
            _launch = CatapultMath.EstimatedLaunchPoint(_catapult, _net);
            _params = CatapultMath.ParamsFor(_catapult,
                CatapultMath.ProjectileFor(_catapult, _catapult.m_loadedItem ?? _catapult.m_defaultAmmo?.m_itemData));

            // Maximum range is measured over flat ground at the catapult's base height.
            float groundDy = CatapultMath.GroundHeight(_catapult.transform.position) - _launch.y;
            if (!Ballistics.RangeAtHeight(_params, CatapultMath.MaxSpeed(_catapult), groundDy, out _absMaxR, out _))
                _absMaxR = 10f;
            _minR = Mathf.Max(1f, _absMaxR * ModConfig.MinRangeFraction);
            _maxR = Mathf.Max(_minR + 0.1f, _absMaxR * ModConfig.MaxRangeFraction);
        }

        private void UpdateMarker(float dt)
        {
            float sens = ModConfig.MouseSensitivity.Value * 0.5f;
            Vector2 m = _mouseAccum;
            _mouseAccum = Vector2.zero;

            float lateral = m.x * sens + _moveInput.x * ModConfig.KeyboardAimSpeed.Value * dt;
            float forward = m.y * sens + _moveInput.z * ModConfig.KeyboardAimSpeed.Value * dt;

            _dist = Mathf.Clamp(_dist + forward, _minR, _maxR);
            _yawDeg += Mathf.Rad2Deg * (lateral / Mathf.Max(_dist, 1f));
            float cone = ModConfig.AimConeDegrees.Value;
            _yawDeg = Mathf.Clamp(_yawDeg, -cone, cone);

            float scroll = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                float lo = Mathf.Min(ModConfig.CameraMinHeight.Value, ModConfig.CameraMaxHeight.Value);
                float hi = Mathf.Max(ModConfig.CameraMinHeight.Value, ModConfig.CameraMaxHeight.Value);
                _camHeight = Mathf.Clamp(_camHeight * (1f - Mathf.Sign(scroll) * 0.1f), lo, hi);
            }
        }

        private Vector3 DirAt(float yawDeg) => Quaternion.AngleAxis(yawDeg, Vector3.up) * _front;

        private Vector3 GroundPoint(float yawDeg, float dist)
        {
            Vector3 p = _launch + DirAt(yawDeg) * dist;
            p.y = CatapultMath.GroundHeight(p);
            return p;
        }

        private void UpdateTarget()
        {
            _target = GroundPoint(_yawDeg, _dist);
            _targetValid = CatapultMath.SolveVelocity(_params, _launch, _target, CatapultMath.MaxSpeed(_catapult),
                out _, out _flightTime);
        }

        private float SpreadRadius() =>
            _dist * Mathf.Tan(ModConfig.SpreadConeDegrees.Value * Mathf.Deg2Rad);

        private void TryFire()
        {
            if (Time.time < _fireCooldownUntil) return;
            if (!CatapultMath.IsLoaded(_catapult))
            {
                _player.Message(MessageHud.MessageType.Center, "The catapult is not loaded");
                return;
            }
            if (!_targetValid)
            {
                _player.Message(MessageHud.MessageType.Center, "Target out of reach");
                return;
            }

            // Random landing point inside the spread circle, chosen once by the aimer so every client agrees.
            Ballistics.RandomInDisc(Random.value, Random.value, SpreadRadius(), out float ox, out float oz);
            Vector3 right = Vector3.Cross(Vector3.up, DirAt(_yawDeg));
            Vector3 aim = _target + right * ox + DirAt(_yawDeg) * oz;
            aim.y = CatapultMath.GroundHeight(aim);

            _net.SendFire(aim);
            _fireCooldownUntil = Time.time + Mathf.Max(0.5f, _catapult.m_armAnimationTime);
        }

        // ---------------------------------------------------------------- camera (called from GameCamera patch)

        internal void UpdateCamera(GameCamera cam, float dt)
        {
            float pitch = Mathf.Clamp(ModConfig.CameraPitch.Value, 30f, 90f);
            float back = pitch >= 89.9f ? 0f : _camHeight / Mathf.Tan(pitch * Mathf.Deg2Rad);
            Vector3 focus = _target;
            Vector3 wantPos = focus - _front * back + Vector3.up * _camHeight;
            Quaternion wantRot = Quaternion.LookRotation((focus - wantPos).normalized,
                pitch >= 89.9f ? _front : Vector3.up);

            if (!_camInit)
            {
                _camPos = cam.transform.position;
                _camRot = cam.transform.rotation;
                _camInit = true;
            }
            float k = 1f - Mathf.Exp(-ModConfig.CameraSmoothing.Value * dt);
            _camPos = Vector3.Lerp(_camPos, wantPos, k);
            _camRot = Quaternion.Slerp(_camRot, wantRot, k);

            cam.transform.position = _camPos;
            cam.transform.rotation = _camRot;
            if (cam.m_camera != null) cam.m_camera.fieldOfView = cam.m_fov;
            if (cam.m_skyCamera != null) cam.m_skyCamera.fieldOfView = cam.m_fov;
        }

        // ---------------------------------------------------------------- drawing

        private static Material LineMaterial()
        {
            if (s_lineMat != null) return s_lineMat;
            Shader sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (sh == null)
            {
                Plugin.Log.LogWarning("No line shader found; aim overlay will not be drawn.");
                return null;
            }
            s_lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (s_lineMat.HasProperty("_ZTest")) s_lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            if (s_lineMat.HasProperty("_ZWrite")) s_lineMat.SetInt("_ZWrite", 0);
            if (s_lineMat.HasProperty("_Cull")) s_lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (s_lineMat.HasProperty("_SrcBlend")) s_lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (s_lineMat.HasProperty("_DstBlend")) s_lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            s_lineMat.renderQueue = 4000;
            return s_lineMat;
        }

        private LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject("CatapultRework_" + name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = LineMaterial();
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        private void CreateLines()
        {
            _sectorLine = MakeLine("Sector", 0.25f);
            _sectorLine.loop = true;
            _spreadLine = MakeLine("Spread", 0.2f);
            _spreadLine.loop = true;
            _markerLine = MakeLine("Marker", 0.15f);
            _pathLine = MakeLine("Path", 0.12f);
        }

        private void DestroyLines()
        {
            foreach (LineRenderer lr in new[] { _sectorLine, _spreadLine, _markerLine, _pathLine })
                if (lr != null) Destroy(lr.gameObject);
            _sectorLine = _spreadLine = _markerLine = _pathLine = null;
        }

        private static void SetColor(LineRenderer lr, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
        }

        private const float LineLift = 0.4f;

        private void UpdateLines()
        {
            if (_sectorLine == null) return;
            Color ok = new Color(1f, 0.85f, 0.2f, 0.9f);
            Color bad = new Color(1f, 0.25f, 0.2f, 0.9f);
            Color sector = new Color(1f, 1f, 1f, 0.6f);

            // Sector outline: near arc, far arc (reversed), closed loop.
            float cone = ModConfig.AimConeDegrees.Value;
            const int arc = 24;
            _sectorLine.positionCount = arc * 2 + 2;
            int i = 0;
            for (int s = 0; s <= arc; s++)
                _sectorLine.SetPosition(i++, GroundPoint(Mathf.Lerp(-cone, cone, s / (float)arc), _minR) + Vector3.up * LineLift);
            for (int s = arc; s >= 0; s--)
                _sectorLine.SetPosition(i++, GroundPoint(Mathf.Lerp(-cone, cone, s / (float)arc), _maxR) + Vector3.up * LineLift);
            SetColor(_sectorLine, sector);

            // Spread circle and a small marker ring.
            Color col = _targetValid ? ok : bad;
            DrawCircle(_spreadLine, _target, Mathf.Max(SpreadRadius(), 0.5f), 32);
            SetColor(_spreadLine, col);
            DrawCircle(_markerLine, _target, 0.6f, 12);
            _markerLine.loop = true;
            SetColor(_markerLine, col);

            // Predicted path.
            if (ModConfig.ShowTrajectory.Value && _targetValid &&
                Ballistics.SolveSpeed(_params, HorizontalDist(_launch, _target), _target.y - _launch.y,
                    CatapultMath.MaxSpeed(_catapult), out float speed, out _))
            {
                Vector3 d = _target - _launch;
                Vector3 dirH = new Vector3(d.x, 0f, d.z).normalized;
                int n = Ballistics.Sample(_params, speed, _xs, _ys, _target.y - _launch.y);
                _pathLine.positionCount = n;
                for (int k = 0; k < n; k++)
                    _pathLine.SetPosition(k, _launch + dirH * _xs[k] + Vector3.up * _ys[k]);
                SetColor(_pathLine, new Color(1f, 1f, 1f, 0.8f));
            }
            else
            {
                _pathLine.positionCount = 0;
            }
        }

        private static float HorizontalDist(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;

        private static void DrawCircle(LineRenderer lr, Vector3 center, float radius, int segments)
        {
            lr.loop = true;
            lr.positionCount = segments;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                p.y = CatapultMath.GroundHeight(p) + LineLift;
                lr.SetPosition(s, p);
            }
        }

        private void OnGUI()
        {
            if (!ModConfig.ShowInfoText.Value || Active != this || _catapult == null) return;
            string ammo = CatapultMath.IsLoaded(_catapult)
                ? "Loaded: " + Localization.instance.Localize(_catapult.m_loadedItem.m_shared.m_name)
                : "Not loaded";
            string state = _targetValid ? $"Flight time {_flightTime:0.0} s" : "Out of reach";
            string text =
                $"Distance {_dist:0} m  ({_dist / Mathf.Max(_absMaxR, 1f) * 100f:0}% of max {_absMaxR:0} m)\n" +
                $"Spread radius {SpreadRadius():0.0} m   {state}\n{ammo}\n" +
                "LMB fire   Mouse/WASD aim   Wheel zoom   RMB/Jump/Esc exit";
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperLeft };
            style.normal.textColor = Color.black;
            Rect r = new Rect(21, 21, 700, 120);
            GUI.Label(r, text, style);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20, 20, 700, 120), text, style);
        }
    }
}
