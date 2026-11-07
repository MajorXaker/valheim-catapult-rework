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
        private bool _damaged;
        private float _damageTaken;
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
            if (Active != null) { ModLog.Debug("Enter refused: an aiming session is already active"); return false; }
            if (player.GetDoodadController() != null) { ModLog.Debug("Enter refused: player already controls " + player.GetDoodadController()); return false; }
            if (player.IsEncumbered()) { ModLog.Debug("Enter refused: player is encumbered"); return false; }
            if (player.InPlaceMode()) { ModLog.Debug("Enter refused: player is in build mode"); return false; }

            CatapultNet net = c.GetComponent<CatapultNet>();
            if (net == null) { ModLog.Warn("Enter refused: catapult has no CatapultNet component"); return false; }
            if (net.IsAimedByOther(player.GetPlayerID()))
            {
                ModLog.Debug("Enter refused: another player holds the aiming lock");
                player.Message(MessageHud.MessageType.Center, "Someone else is aiming this catapult");
                return false;
            }

            AimController ctrl = c.gameObject.AddComponent<AimController>();
            ctrl.Setup(c, net, player);
            Active = ctrl;
            player.StartDoodadControl(ctrl);
            ModLog.Debug($"Aim start: player at {ModLog.F(player.transform.position)}, load point {ModLog.F(c.m_loadPoint.transform.position)}, " +
                         $"distance to load point {ModLog.F(Vector3.Distance(player.transform.position, c.m_loadPoint.transform.position))} m, " +
                         $"to catapult {ModLog.F(Vector3.Distance(player.transform.position, c.transform.position))} m, " +
                         $"owner={(c.m_nview != null && c.m_nview.IsOwner())}, loaded={(c.m_loadedItem != null ? c.m_loadedItem.m_shared.m_name : "no")}");
            ctrl.LogGeometry();
            return true;
        }

        private void Setup(Catapult c, CatapultNet net, Player player)
        {
            _catapult = c;
            _net = net;
            _player = player;
            _camHeight = ModConfig.CameraHeight.Value;
            float startDist = Vector3.Distance(player.transform.position, c.transform.position);
            _leaveDistance = Mathf.Max(startDist, player.m_maxInteractDistance) + ExtraLeaveDistance;
            _player.m_onDamaged = (System.Action<float, Character>)System.Delegate.Combine(_player.m_onDamaged, new System.Action<float, Character>(OnPlayerDamaged));
            RecomputeGeometry();
            _yawDeg = 0f;
            _dist = Mathf.Lerp(_minR, _maxR, 0.5f);
            _net.SendAimer(player.GetPlayerID());
            _nextHeartbeat = Time.time + CatapultNet.AimerHeartbeat;
            CreateLines();
        }

        private bool _stopped;

        public void Stop(string reason)
        {
            ModLog.Debug("Aim stop: " + reason);
            _stopping = true;
            // StopDoodadControl only calls OnUseStop while IsValid() is true, so always clean up afterwards.
            if (_player != null && _player.GetDoodadController() == (IDoodadController)this)
                _player.StopDoodadControl();
            Cleanup();
        }

        private void Cleanup()
        {
            if (_stopped) return;
            _stopped = true;
            UnhookDamage();
            if (Active == this) Active = null;
            if (_net != null) _net.SendAimer(0L);
            DestroyLines();
            if (this != null) Destroy(this);
        }

        private void OnDestroy()
        {
            if (!_stopped) ModLog.Debug("Aim stop: controller destroyed (catapult removed or unloaded)");
            UnhookDamage();
            if (Active == this) Active = null;
            DestroyLines();
        }

        private void UnhookDamage()
        {
            if (_player != null)
                _player.m_onDamaged = (System.Action<float, Character>)System.Delegate.Remove(_player.m_onDamaged, new System.Action<float, Character>(OnPlayerDamaged));
        }

        // Real hits only. (Max health shrinking as food wears off also lowers current health, so polling health is wrong.)
        private void OnPlayerDamaged(float damage, Character attacker)
        {
            if (damage <= 0.05f) return;
            _damaged = true;
            _damageTaken += damage;
        }

        // ---------------------------------------------------------------- IDoodadController

        public void OnUseStop(Player player)
        {
            if (!_stopping) ModLog.Debug("Aim stop: released by the game (right click / jump / dodge / Use key)");
            Cleanup();
        }

        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block) { }

        public Component GetControlledComponent() => _catapult;

        // Vanilla stops control when the player's feet are farther than m_maxInteractDistance (5 m) from this
        // point, which can already be true when aiming starts (hover range is measured from the eyes).
        // Return the player's own position and do a more lenient check in Update instead.
        public Vector3 GetPosition() =>
            _player != null ? _player.transform.position : transform.position;

        private const float ExtraLeaveDistance = 3f;
        private float _leaveDistance;
        private bool _stopping;

        // Kept minimal on purpose: if this returns false the game drops control silently, so all exit
        // conditions (legs, distance, damage...) are checked in Update where the reason can be logged.
        public bool IsValid() => this != null && _catapult != null && !_stopped;

        // ---------------------------------------------------------------- input from patches

        internal void AddMouse(Vector2 delta) => _mouseAccum += delta;
        internal void SetMove(Vector3 move) => _moveInput = move;
        internal void RequestFire() => _fireRequested = true;

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (_player != null && _player.GetDoodadController() != (IDoodadController)this)
            {
                ModLog.Debug("Aim stop: control was released by the game without notice");
                Cleanup();
                return;
            }
            if (_player == null || _player.IsDead()) { Stop("player dead or missing"); return; }
            if (_catapult == null) { Stop("catapult gone"); return; }
            if (!ModConfig.Enabled.Value) { Stop("mod disabled by server config"); return; }
            if (!CatapultMath.LegsReady(_catapult)) { Stop("legs raised or moving"); return; }

            // Measured to the catapult body: the load point sits on the arm and swings away when firing.
            float d = Vector3.Distance(_player.transform.position, _catapult.transform.position);
            if (d > _leaveDistance) { Stop($"player too far from catapult ({ModLog.F(d)} m > {ModLog.F(_leaveDistance)} m)"); return; }

            if (_damaged)
            {
                _damaged = false;
                if (ModConfig.ExitOnDamage.Value) { Stop($"took {ModLog.F(_damageTaken)} damage"); return; }
            }

            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                Stop("Esc pressed");
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

        internal void LogGeometry()
        {
            if (!ModLog.DebugEnabled) return;
            ModLog.Debug($"Geometry: front {ModLog.F(_front)}, elevation {ModLog.F(_params.ElevationRad * Mathf.Rad2Deg)}°, " +
                         $"vanilla speed {ModLog.F(CatapultMath.VanillaSpeed(_catapult))}, max speed {ModLog.F(CatapultMath.MaxSpeed(_catapult))}, " +
                         $"gravity {ModLog.F(_params.Gravity)}, drag {ModLog.F(_params.Drag)}, ttl {ModLog.F(_params.MaxTime)}, dt {ModLog.F(_params.Dt)}, " +
                         $"est. launch {ModLog.F(_launch)} (cached={_net.HasCachedLaunchOffset}), max range {ModLog.F(_absMaxR)} m, " +
                         $"allowed {ModLog.F(_minR)}–{ModLog.F(_maxR)} m, cone ±{ModLog.F(ModConfig.AimConeDegrees.Value)}°");
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
                ModLog.Debug($"Fire refused: not loaded (item={(_catapult.m_loadedItem != null)}, armAnimTime={ModLog.F(_catapult.m_armAnimTime)})");
                _player.Message(MessageHud.MessageType.Center, "The catapult is not loaded");
                return;
            }
            if (!_targetValid)
            {
                ModLog.Debug($"Fire refused: target {ModLog.F(_target)} unreachable");
                _player.Message(MessageHud.MessageType.Center, "Target out of reach");
                return;
            }

            // Random landing point inside the spread circle, chosen once by the aimer so every client agrees.
            Ballistics.RandomInDisc(Random.value, Random.value, SpreadRadius(), out float ox, out float oz);
            Vector3 right = Vector3.Cross(Vector3.up, DirAt(_yawDeg));
            Vector3 aim = _target + right * ox + DirAt(_yawDeg) * oz;
            aim.y = CatapultMath.GroundHeight(aim);

            ModLog.Debug($"Fire: marker {ModLog.F(_target)} (yaw {ModLog.F(_yawDeg)}°, dist {ModLog.F(_dist)} m), " +
                         $"spread radius {ModLog.F(SpreadRadius())} m, chosen point {ModLog.F(aim)}, est. launch {ModLog.F(_launch)}");
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
