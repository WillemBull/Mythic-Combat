using System.Collections.Generic;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Match;
using Godsbound.Core.Combat;
using Godsbound.Data;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Existing artwork above two footprint markers, with live health, names and fortress shots.
    /// </summary>
    /// <remarks>
    /// <para>Geometry comes from the browser export. Footprint markers remain as the missing-art
    /// fallback and become dark rubble when destroyed. Sprite scale stays upright.</para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("Godsbound/Buildings View")]
    public sealed class BuildingsView : MonoBehaviour
    {
        [Tooltip("World units per hex circumradius. Match the Board View.")]
        [SerializeField] private float unitsPerHex = 1f;

        [Tooltip("Marker size relative to a hex.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float markerFill = 0.62f;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private Mesh _mesh;
        private Material _material;
        private BuildingMap _buildings;
        private MatchState match;
        private System.Func<int,string> faction = side => side == 0 ? "egypt" : "china";
        private BuildingArtCatalog catalog;
        private Sprite pixel;
        private Texture2D pixelTexture;
        private Sprite puff;
        private Texture2D puffTexture;
        private Material tracerMaterial;
        private sealed class Body
        {
            public GameObject Visual;
            public SpriteRenderer Art, Health, Back;
            public TextMesh Label;
            public readonly List<MeshRenderer> Markers = new List<MeshRenderer>();
            // Roadmap 6.6's damage states. Built once per building and parked inactive; a whole
            // building never pays for them beyond the SetActive(false).
            public readonly List<SpriteRenderer> Cracks = new List<SpriteRenderer>();
            public readonly List<SpriteRenderer> Smoke = new List<SpriteRenderer>();
            // Resolved at Rebuild: faction and type are fixed for a building's lifetime, so the
            // per-frame catalog scan (and the two strings it built to search with) was waste.
            public BuildingArtEntry Entry;
        }
        private sealed class Trace { public GameObject Visual; public float Until; }
        private readonly Dictionary<Building,Body> bodies = new Dictionary<Building,Body>();
        private readonly List<Trace> traces = new List<Trace>();
        // Created lazily inside Sync, not in a field initializer: MonoBehaviour constructors run
        // during deserialization, where engine objects should not be built.
        private MaterialPropertyBlock syncBlock;
        public int TracerCount => traces.Count;

        /// <summary>
        /// While a card is being dragged, the side whose buildings can start a route — or -1 for
        /// none (U40). They pulse gold so the player can SEE where to drag, which the browser only
        /// ever said in words. Presentation only: it changes nothing about what is legal.
        /// </summary>
        public int DeployHighlight { get; set; } = -1;
        /// <summary>Gold, twice a second, never fully off: a hint, not an alarm.</summary>
        public static float Pulse(float elapsed) => 0.65f + 0.35f * Mathf.Sin(elapsed * 6.3f);
        public Transform BodyOf(Building b) => bodies.TryGetValue(b,out var body) ? body.Visual.transform : null;

        /// <summary>The buildings being drawn. Loaded from the export on first use.</summary>
        public BuildingMap Buildings
        {
            get
            {
                if (_buildings == null)
                    _buildings = BuildingMap.FromExport(GameDataLoader.Load().Buildings);
                return _buildings;
            }
        }

        public int MarkerCount => _markers.Count;

        public void Bind(BuildingMap buildings)
        {
            Unsubscribe(); match = null;
            _buildings = buildings ?? throw new System.ArgumentNullException(nameof(buildings));
            Rebuild();
        }

        public void Bind(MatchState state, System.Func<int,string> factionForSide = null)
        {
            Unsubscribe(); match = state ?? throw new System.ArgumentNullException(nameof(state));
            _buildings = state.Buildings;
            faction = factionForSide ?? state.UnitContext.FactionForSide;
            Rebuild(); Subscribe();
        }

        private void OnEnable() { Rebuild(); Subscribe(); }
        private void OnDisable() { Unsubscribe(); Clear(); }
        // No LateUpdate: MatchController.Advance syncs once per frame after the simulation step.
        // A second sync from here doubled the per-frame work for nothing.
        private void Subscribe() { Unsubscribe(); if(match != null && isActiveAndEnabled) match.Fortresses.Fired += Shot; }
        private void Unsubscribe() { if(match != null) match.Fortresses.Fired -= Shot; }

        public void Rebuild()
        {
            Clear();

            if (_mesh == null) _mesh = HexMesh.Create(1f);
            if (_material == null) _material = CreateMaterial();
            EnsureArt();

            var layout = BoardWorld.UnitLayout();
            var block = new MaterialPropertyBlock();

            foreach (var b in Buildings.All)
            {
                var body = new Body { Visual = new GameObject($"Bld_{b.Side}_{b.Type}_Visual") { hideFlags = HideFlags.DontSave } };
                body.Visual.transform.SetParent(transform,false);
                string key = Building.ToKey(b.Type), side = faction(b.Side);
                body.Entry = catalog.Entry(side,key);
                body.Art = SpritePart(body.Visual.transform,"Art",catalog.Resolve(side,key));
                for(int i=0;i<BuildingDamageState.Cracks;i++)
                {
                    var crack = SpritePart(body.Visual.transform,"Crack"+i,pixel);
                    crack.gameObject.SetActive(false); body.Cracks.Add(crack);
                }
                for(int i=0;i<BuildingDamageState.Smokes;i++)
                {
                    var smoke = SpritePart(body.Visual.transform,"Smoke"+i,puff);
                    smoke.gameObject.SetActive(false); body.Smoke.Add(smoke);
                }
                body.Back = SpritePart(body.Visual.transform,"HealthBackground",pixel);
                body.Health = SpritePart(body.Visual.transform,"Health",pixel);
                var label = new GameObject("Name"); label.transform.SetParent(body.Visual.transform,false);
                body.Label = label.AddComponent<TextMesh>(); body.Label.text = b.Name.ToUpperInvariant();
                body.Label.anchor = TextAnchor.MiddleCenter; body.Label.alignment = TextAlignment.Center;
                body.Label.fontSize = 48; body.Label.characterSize = 0.075f;
                body.Label.color = new Color(0.95f,0.9f,0.78f);
                body.Label.GetComponent<MeshRenderer>().sortingOrder = -997;
                bodies.Add(b,body);
                foreach (var h in b.Hexes())
                {
                    var go = new GameObject($"Bld_{b.Side}_{b.Type}_{h.C}_{h.R}")
                    {
                        hideFlags = HideFlags.DontSave
                    };
                    go.transform.SetParent(transform, false);

                    // Slightly in front of the board so the marker is not z-fighting the cell.
                    var p = BoardWorld.CenterOf(h, layout, unitsPerHex);
                    go.transform.localPosition = new Vector3(p.x, p.y, p.z - 0.05f);
                    go.transform.localScale = BoardWorld.GroundScale(unitsPerHex * markerFill);

                    go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    block.Clear();
                    block.SetColor(BaseColorId, ColourFor(b));
                    block.SetColor(LegacyColorId, ColourFor(b));
                    mr.SetPropertyBlock(block);

                    _markers.Add(go);
                    body.Markers.Add(mr);
                }
            }
            Sync();
        }

        private void EnsureArt()
        {
            if(catalog != null) return;
            catalog = new BuildingArtCatalog(BuildingPresentationData.Load());
            pixelTexture = new Texture2D(1,1);pixelTexture.SetPixel(0,0,Color.white);pixelTexture.Apply();
            pixel = Sprite.Create(pixelTexture,new Rect(0,0,1,1),new Vector2(0.5f,0.5f),1);
            puffTexture = Disc(32); puff = Sprite.Create(puffTexture,new Rect(0,0,32,32),new Vector2(0.5f,0.5f),32);
        }
        /// <summary>A soft white disc, so a smoke puff is round like the browser's <c>arc</c> and not a square.</summary>
        private static Texture2D Disc(int size)
        {
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false) { hideFlags=HideFlags.DontSave };
            float r = size/2f;
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float dx=x+0.5f-r, dy=y+0.5f-r;
                float d=Mathf.Sqrt(dx*dx+dy*dy)/r;
                texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(1f-d*d)));
            }
            texture.Apply(); return texture;
        }
        private static SpriteRenderer SpritePart(Transform parent,string name,Sprite sprite)
        {
            var go = new GameObject(name); go.transform.SetParent(parent,false);
            var r = go.AddComponent<SpriteRenderer>(); r.sprite = sprite; return r;
        }
        public void Sync()
        {
            if(catalog == null) return;
            var data = catalog.Data;
            var layout = BoardWorld.UnitLayout();
            var block = syncBlock ?? (syncBlock = new MaterialPropertyBlock());
            foreach(var pair in bodies)
            {
                var b = pair.Key; var body = pair.Value;
                var a = BoardWorld.CenterOf(b.HexA,layout,unitsPerHex);
                var z = BoardWorld.CenterOf(b.HexB,layout,unitsPerHex);
                body.Visual.transform.localPosition = (a+z)*0.5f + new Vector3(0,0,-0.1f);
                var entry = body.Entry;
                bool arted = body.Art.sprite != null;
                float size = data.canvas*(entry?.scale ?? 1f)*unitsPerHex;
                body.Art.gameObject.SetActive(arted && !b.Dead);
                body.Art.transform.localPosition = new Vector3((0.5f-data.anchorX)*size,(data.anchorY-0.5f)*size,0);
                body.Art.transform.localScale = BoardWorld.UprightScale(size);
                body.Art.flipX = (b.HexA.C & 1) != 0;
                body.Art.sortingOrder = -1000;
                // Bastet's Guardian's Grace: a warded building glows gold while it cannot be damaged.
                body.Art.color = match != null && match.Elapsed < b.InvulnerableUntil ? new Color(1f, 0.92f, 0.55f) : Color.white;
                // A deploy target outranks the ward tint: it is an instruction, and it is transient.
                if (!b.Dead && b.Side == DeployHighlight)
                {
                    float pulse = Pulse(match?.Elapsed ?? 0f);
                    body.Art.color = Color.Lerp(body.Art.color, new Color(1f, 0.85f, 0.35f), pulse);
                }
                Wear(b,body,size,arted);
                float barY = arted ? data.anchorY*size+data.healthGap*unitsPerHex : 1.15f*unitsPerHex;
                float width = data.healthWidth*unitsPerHex;
                float ratio = Mathf.Clamp01(b.Hp/b.MaxHp);
                body.Back.gameObject.SetActive(!b.Dead); body.Health.gameObject.SetActive(!b.Dead);
                body.Back.transform.localPosition = new Vector3(0,barY,-0.01f);
                body.Back.transform.localScale = new Vector3(width,0.12f*unitsPerHex,1);
                body.Back.color = new Color(0.05f,0.05f,0.06f);body.Back.sortingOrder = -999;
                body.Health.transform.localPosition = new Vector3(width*(ratio-1f)/2f,barY,-0.02f);
                body.Health.transform.localScale = new Vector3(width*ratio,0.08f*unitsPerHex,1);
                body.Health.color = b.Side == 0 ? new Color(0.91f,0.72f,0.29f) : new Color(0.9f,0.3f,0.24f);
                body.Health.sortingOrder = -998;
                float labelY = arted ? -(1-data.anchorY)*size+(data.labelInset-data.labelHeight/2f)*unitsPerHex : -1.11f*unitsPerHex;
                body.Label.transform.localPosition = new Vector3(0,labelY,-0.02f);
                body.Label.transform.localScale = BoardWorld.UprightScale(unitsPerHex);
                var colour = ColourFor(b);
                block.Clear();block.SetColor(BaseColorId,colour);block.SetColor(LegacyColorId,colour);
                foreach(var marker in body.Markers) marker.SetPropertyBlock(block);
            }
            for(int i=traces.Count-1;i>=0;i--)
                if(match == null || match.Elapsed >= traces[i].Until)
                { UnitArtCatalog.Release(traces[i].Visual);traces.RemoveAt(i); }
        }
        /// <summary>
        /// Roadmap 6.6: dark cracks over a building below half health, smoke puffs below a quarter.
        /// Crack placement is deterministic in the building's own hex (<see cref="BuildingDamageState.Jitter"/>),
        /// so a wall's damage does not crawl around between frames. The puff radii and grey come from
        /// the browser's rubble drawing; where they sit is ours, because the browser only ever smoked
        /// a building that was already dead.
        /// </summary>
        private void Wear(Building b,Body body,float size,bool arted)
        {
            var wear = arted && !b.Dead ? BuildingDamageState.For(b) : BuildingWear.Whole;
            var art = body.Art.transform.localPosition;
            for(int i=0;i<body.Cracks.Count;i++)
            {
                var crack = body.Cracks[i];
                crack.gameObject.SetActive(wear != BuildingWear.Whole);
                if(wear == BuildingWear.Whole) continue;
                float jx = BuildingDamageState.Jitter(b,i,0), jy = BuildingDamageState.Jitter(b,i,1);
                float jl = BuildingDamageState.Jitter(b,i,2), ja = BuildingDamageState.Jitter(b,i,3);
                crack.transform.localPosition = art + new Vector3((jx-0.5f)*0.55f*size,(jy-0.6f)*0.5f*size,-0.005f);
                crack.transform.localScale = new Vector3(0.028f*size,(0.16f+0.14f*jl)*size,1f);
                crack.transform.localRotation = Quaternion.Euler(0,0,ja*70f-35f);
                crack.color = new Color(0.08f,0.07f,0.07f,0.72f);
                crack.sortingOrder = -999;
            }
            for(int i=0;i<body.Smoke.Count;i++)
            {
                var smoke = body.Smoke[i];
                smoke.gameObject.SetActive(wear == BuildingWear.Burning);
                if(wear != BuildingWear.Burning) continue;
                float jx = BuildingDamageState.Jitter(b,i,4);
                // Browser rubble puffs: radius 3.5 and 2.6 at s=HEX/24, grey at 45% alpha.
                float radius = (i == 0 ? 3.5f : 2.6f)/24f*unitsPerHex*2f;
                float phase = match == null ? 0f : Mathf.Repeat(match.Elapsed*0.45f + i*0.33f,1f);
                float top = art.y + 0.5f*size;
                smoke.transform.localPosition = new Vector3(art.x+(jx-0.5f)*0.4f*size,top+(0.1f+0.5f*phase)*size,-0.006f);
                smoke.transform.localScale = Vector3.one*radius*(1f+0.6f*phase);
                smoke.color = new Color(0.51f,0.51f,0.51f,0.45f*(1f-phase));
                smoke.sortingOrder = -998;
            }
        }

        private void Shot(FortressShot shot)
        {
            if(tracerMaterial == null) tracerMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags=HideFlags.DontSave };
            var go = new GameObject("Bld_Tracer") { hideFlags=HideFlags.DontSave };
            go.transform.SetParent(transform,false);
            var line = go.AddComponent<LineRenderer>();line.useWorldSpace=false;
            line.sharedMaterial=tracerMaterial;line.positionCount=2;line.sortingOrder=30000;
            var layout=BoardWorld.UnitLayout();
            var from=(BoardWorld.CenterOf(shot.Source.HexA,layout,unitsPerHex)+BoardWorld.CenterOf(shot.Source.HexB,layout,unitsPerHex))*0.5f;
            var to=BoardWorld.ToWorld(shot.Destination,unitsPerHex);from.z=to.z=-0.3f;
            line.SetPosition(0,from);line.SetPosition(1,to);line.startWidth=line.endWidth=0.045f*unitsPerHex;
            line.startColor=line.endColor=shot.Source.Side==0 ? new Color(1,0.88f,0.54f) : new Color(1,0.6f,0.48f);
            traces.Add(new Trace {Visual=go,Until=shot.At+shot.Life});
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        private static Color ColourFor(Building b)
        {
            Color c;
            switch (b.Type)
            {
                case BuildingType.City: c = new Color(0.86f, 0.74f, 0.36f); break;   // gold
                case BuildingType.Temple: c = new Color(0.62f, 0.45f, 0.85f); break; // favor purple
                default: c = new Color(0.72f, 0.30f, 0.26f); break;                  // fortress red
            }

            // The AI's buildings read darker, so the two halves are distinguishable.
            if (b.Side == 1) c *= 0.72f;

            // Rubble dims hard — it no longer blocks routes, and that should be visible.
            if (b.Dead) c = Color.Lerp(c, new Color(0.15f, 0.15f, 0.16f), 0.75f);

            c.a = 1f;
            return c;
        }

        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            return new Material(shader) { name = "GodsboundBuilding", hideFlags = HideFlags.DontSave };
        }

        private void Clear()
        {
            _markers.Clear();
            bodies.Clear(); traces.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (!child.name.StartsWith("Bld_")) continue;
                child.SetActive(false); UnitArtCatalog.Release(child);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe(); Clear(); catalog?.Dispose();
            UnitArtCatalog.Release(pixel); UnitArtCatalog.Release(pixelTexture);
            UnitArtCatalog.Release(puff); UnitArtCatalog.Release(puffTexture);
            UnitArtCatalog.Release(_mesh); UnitArtCatalog.Release(_material); UnitArtCatalog.Release(tracerMaterial);
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Rebuild();
            };
#else
            Rebuild();
#endif
        }
    }
}
