using System.Collections.Generic;
using UnityEngine;

public sealed class JetskiRunnerGame : MonoBehaviour
{
    public enum EnvironmentPreset { TropicalRun, PirateCove, IceCave }
    [SerializeField] EnvironmentPreset environment = EnvironmentPreset.TropicalRun;
    [Header("Run")]
    [SerializeField] float forwardSpeed = 12f;
    [SerializeField] float laneWidth = 2.3f;
    [SerializeField] float spawnAhead = 72f;
    [SerializeField] Shader tropicalWaterShader;
    [SerializeField] GameObject goldPrefab;
    [SerializeField] GameObject riderPrefab;
    [Header("Pirate Escape assets")]
    [SerializeField] GameObject[] obstaclePrefabs;
    [SerializeField] GameObject fuelPrefab;
    [SerializeField] GameObject rampPrefab;
    [SerializeField] GameObject pirateShipPrefab;
    [SerializeField] float shipSailSpeed = 7f;
    [SerializeField] float shipLength = 8f;
    [SerializeField] GameObject sideRockPrefab;
    [SerializeField] GameObject dockBarrelPrefab;
    [SerializeField] Shader skyShader;
    [SerializeField] Material sprayMaterial;
    [SerializeField] GameObject iceTunnelPrefab;
    [SerializeField] float iceTunnelLength = 24f;
    [SerializeField] float boostMultiplier = 1.6f;
    [SerializeField] float boostDuration = 3.5f;
    [SerializeField] float jumpDuration = 1.1f;
    [SerializeField] float jumpHeight = 2.2f;

    const float WaterY = -.42f;
    Transform player, movingWorld;
    TropicalScenery scenery;
    readonly float[] nextShoreAt = { -12f, -2f };
    readonly List<ParticleSystem> sprays = new();
    Camera gameCamera;
    readonly List<Transform> recycledWater = new();
    readonly List<Transform> iceSegments = new();
    readonly List<GameObject> spawned = new();
    Material sea, seaLight, foam, gold, orange, cyan, charcoal, skin, wood, rock, palmLeaf;
    Material waterSurfaceMaterial;
    static Shader fallbackShader;
    int lane = 1, coins;
    float distanceTravelled, nextRowAt = 32f, laneBlend, boostLeft, jumpTime = -1f;
    bool crashed, landingCued;
    PirateRider rider;
    Vector2 touchStart;
    GUIStyle distanceStyle, coinStyle, hintStyle, boostStyle, shadowStyle;
    float CurrentSpeed => crashed ? 0f : (forwardSpeed + Mathf.Min(distanceTravelled * .004f, 6f)) * (boostLeft > 0 ? boostMultiplier : 1f);
    float JumpOffset => jumpTime < 0 ? 0f : 4f * jumpHeight * jumpTime * (1f - jumpTime);

    void Start()
    {
        Application.targetFrameRate = 60;
        CreateMaterials();
        CreateLightingAndSky();
        CreateOcean();
        CreatePlayer();
        if (environment == EnvironmentPreset.IceCave) CreateIceCave();
        else CreateShore();
        CreateCamera();
        CreateSpray();
        CreateCoinRow(1, 25f, 4);
        CreateCoinRow(0, 54f, 3);
    }

    void Update()
    {
        if (crashed) { if (RestartPressed()) UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex); return; }
        if (Time.timeScale == 0) return;
        ReadLaneInput();
        float speed = CurrentSpeed;
        distanceTravelled += speed * Time.deltaTime;
        boostLeft = Mathf.Max(0, boostLeft - Time.deltaTime);
        if (jumpTime >= 0)
        {
            jumpTime += Time.deltaTime / jumpDuration;
            if (jumpTime >= .72f && !landingCued) { landingCued = true; if (rider) rider.Land(); }
            if (jumpTime >= 1f) jumpTime = -1f;
        }
        laneBlend = Mathf.MoveTowards(laneBlend, 1f, Time.deltaTime * 5f);
        var desired = new Vector3((lane - 1) * laneWidth, .15f, 0);
        var position = Vector3.Lerp(player.position, desired, 1f - Mathf.Exp(-9f * Time.deltaTime));
        position.y = .15f + JumpOffset + (jumpTime < 0 ? Mathf.Sin(Time.time * 7f) * .04f : 0f);
        player.position = position;
        if (waterSurfaceMaterial != null) { waterSurfaceMaterial.SetFloat("_PlayerX",player.position.x); waterSurfaceMaterial.SetFloat("_Scroll",distanceTravelled); }
        float pitch = jumpTime < 0 ? 0f : Mathf.Lerp(-14f, 10f, jumpTime);
        player.rotation = Quaternion.Slerp(player.rotation, Quaternion.Euler(pitch, 0, Mathf.Clamp((desired.x-player.position.x)*-12f,-13f,13f)), Time.deltaTime*7f);
        movingWorld.position += Vector3.back * (speed * Time.deltaTime);
        foreach (var spray in sprays)
        {
            var velocity = spray.velocityOverLifetime; velocity.z = -speed;
            var emission = spray.emission; emission.enabled = jumpTime < 0;
        }
        FollowCamera();
        SpawnShoreUpTo(distanceTravelled + ShoreAhead);
        RecycleWater();
        RecycleIceTunnel();
        CleanupPassed();
        while (nextRowAt < distanceTravelled + spawnAhead)
        {
            int pattern = Random.Range(0, 10);
            int selected = Random.Range(0, 3);
            bool hazards = nextRowAt > 60f && obstaclePrefabs != null && obstaclePrefabs.Length > 0;
            float rowLength = 0f;
            if (pattern == 0) CreateCoinRow(selected, nextRowAt, Random.Range(3, 5));
            else if (pattern == 1) CreateZigzagRow(selected, nextRowAt);
            else if (hazards && pattern <= 4) CreateObstacleRow(selected, nextRowAt);
            else if (hazards && pattern <= 6 && pirateShipPrefab) { CreateShipRow(selected, nextRowAt); rowLength = shipLength * 2f; }
            else if (hazards && pattern == 7 && rampPrefab) CreateRampRow(selected, nextRowAt);
            else if (pattern == 8 && fuelPrefab) CreateFuelPickup(selected, nextRowAt);
            else CreateCoinRow(selected, nextRowAt, Random.Range(1, 3));
            nextRowAt += rowLength + Random.Range(16f, 24f);
        }
    }

    bool RestartPressed() =>
        Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return) ||
        Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);

    void ReadLaneInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeLane(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeLane(1);
        if (Input.touchCount == 0) return;
        var touch = Input.GetTouch(0);
        if (touch.phase == TouchPhase.Began) touchStart = touch.position;
        if (touch.phase == TouchPhase.Ended)
        {
            float dx = touch.position.x - touchStart.x;
            if (Mathf.Abs(dx) > Screen.width * .08f) ChangeLane(dx > 0 ? 1 : -1);
            else ChangeLane(touch.position.x > Screen.width * .5f ? 1 : -1);
        }
    }
    void ChangeLane(int step)
    {
        int next = Mathf.Clamp(lane + step, 0, 2);
        if (next != lane && rider) rider.Lean(step);
        lane = next;
    }

    void CreateMaterials()
    {
        bool ice = environment == EnvironmentPreset.IceCave;
        sea = Make(ice ? "Ice cave | dark teal water" : "Ocean | tropical blue", ice ? new Color(.005f,.2f,.28f) : new Color(.015f,.39f,.59f), .22f,.48f);
        seaLight = Make("Wave facets", ice ? new Color(.05f,.78f,.78f) : new Color(.03f,.73f,.82f), .12f,.52f);
        foam = Make("Sea foam", new Color(.83f,.97f,1f), 0,.3f);
        gold = Make("Gold", new Color(1f,.57f,.025f), .68f,.78f);
        orange = Make("Jetski orange", new Color(1f,.19f,.025f), .14f,.55f);
        cyan = Make("Jetski turquoise", new Color(.015f,.76f,.84f), .2f,.65f);
        charcoal = Make("Graphite", new Color(.018f,.04f,.065f), .18f,.4f);
        skin = Make("Rider skin", new Color(.53f,.24f,.11f), 0,.35f);
        wood = Make("Pirate ship timber", new Color(.24f,.075f,.025f), 0,.32f);
        rock = Make("Island rock", new Color(.29f,.37f,.39f), 0,.25f);
        palmLeaf = Make("Palm leaves", new Color(.16f,.48f,.16f), 0,.35f);
    }
    Material Make(string n, Color c, float metallic, float smooth)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
        if (shader == null)
        {
            if (fallbackShader == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallbackShader = probe.GetComponent<Renderer>().sharedMaterial.shader;
                Destroy(probe);
            }
            shader = fallbackShader;
        }
        if (shader == null) throw new System.InvalidOperationException("No compatible Unity shader is available for runtime materials.");
        var m = new Material(shader) { name=n, color=c };
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }

    void CreateLightingAndSky()
    {
        bool ice = environment == EnvironmentPreset.IceCave;
        if (ice) { RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.25f,.52f,.62f); }
        else
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.72f,.82f,.92f);
            RenderSettings.ambientEquatorColor = new Color(.6f,.74f,.8f);
            RenderSettings.ambientGroundColor = new Color(.32f,.52f,.58f);
        }
        RenderSettings.fog = true; RenderSettings.fogColor = ice ? new Color(.015f,.18f,.24f) : new Color(.78f,.87f,.93f);
        RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = ice ? 48 : 60; RenderSettings.fogEndDistance = ice ? 145 : 470;
        var sun = new GameObject("Tropical sunlight").AddComponent<Light>();
        sun.type=LightType.Directional; sun.color=ice ? new Color(.36f,.88f,1f) : new Color(1f,.95f,.86f); sun.intensity=ice ? .8f : 1.15f;
        sun.shadows=LightShadows.Soft; sun.shadowStrength=.55f;
        sun.transform.rotation=Quaternion.Euler(48,-32,0);
        QualitySettings.shadowDistance=60;
    }

    void CreateOcean()
    {
        movingWorld = new GameObject("Endless scenery and pickups").transform;
        if (environment == EnvironmentPreset.IceCave)
        {
            if (iceTunnelPrefab) return; // the tunnel segments carry their own glacial water
            var iceWater = GameObject.CreatePrimitive(PrimitiveType.Plane);
            iceWater.name="Ice cave water"; iceWater.transform.position=new Vector3(0,-.42f,67);
            iceWater.transform.localScale=new Vector3(7.4f,1,500f); iceWater.GetComponent<Renderer>().sharedMaterial=sea;
            Destroy(iceWater.GetComponent<Collider>());
        }
        else
        {
            var waterShader = tropicalWaterShader ? tropicalWaterShader : Shader.Find("JetskiRunner/TropicalWater");
            waterSurfaceMaterial = waterShader ? new Material(waterShader) : sea;
            TropicalWaterSurface.Create(waterSurfaceMaterial, -.42f);
        }
    }

    void CreatePlayer()
    {
        player=new GameObject("Player Jetski").transform; player.position=new Vector3(0,.13f,0);
        player.gameObject.AddComponent<Rigidbody>().isKinematic=true;
        player.gameObject.AddComponent<CapsuleCollider>().radius=.78f;
        if (riderPrefab)
        {
            var model=Instantiate(riderPrefab,player);
            // Pirate Escape prefabs are already turned to face +Z and authored with the waterline at y=0.
            model.transform.localPosition=new Vector3(0,WaterY-.15f,0);
            model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=Vector3.one;
            rider=model.GetComponentInChildren<PirateRider>();
            return;
        }
        CreateHullMesh("Shaped low-poly jetski hull",player,charcoal);
        CreateDeckMesh("Turquoise upper deck",player,cyan);
        CreateDeckMesh("Orange nose stripe",player,orange,.58f,.93f,.67f);
        Piece("Padded seat",PrimitiveType.Capsule,player,new Vector3(0,.28f,-.48f),new Vector3(.34f,.12f,.58f),charcoal);
        Piece("Rider jacket",PrimitiveType.Capsule,player,new Vector3(0,.68f,-.13f),new Vector3(.32f,.42f,.28f),orange).transform.localRotation=Quaternion.Euler(34,0,0);
        Piece("Life vest center",PrimitiveType.Cube,player,new Vector3(0,.68f,-.105f),new Vector3(.30f,.31f,.30f),charcoal);
        Piece("Vest back panel",PrimitiveType.Cube,player,new Vector3(0,.72f,-.31f),new Vector3(.38f,.25f,.07f),orange);
        Piece("Rider helmet",PrimitiveType.Sphere,player,new Vector3(0,1.22f,.12f),new Vector3(.30f,.34f,.29f),skin);
        Piece("Helmet crown",PrimitiveType.Sphere,player,new Vector3(0,1.36f,.10f),new Vector3(.31f,.22f,.30f),charcoal);
        Piece("Helmet orange stripe",PrimitiveType.Cube,player,new Vector3(0,1.37f,.16f),new Vector3(.08f,.12f,.28f),orange);
        CreateLimb("Left forearm",player,new Vector3(-.27f,.84f,-.04f),new Vector3(-.34f,.55f,.43f),skin,.105f);
        CreateLimb("Right forearm",player,new Vector3(.27f,.84f,-.04f),new Vector3(.34f,.55f,.43f),skin,.105f);
        CreateLimb("Left leg",player,new Vector3(-.19f,.43f,-.28f),new Vector3(-.31f,.22f,-.70f),charcoal,.145f);
        CreateLimb("Right leg",player,new Vector3(.19f,.43f,-.28f),new Vector3(.31f,.22f,-.70f),charcoal,.145f);
        Piece("Handlebar",PrimitiveType.Cylinder,player,new Vector3(0,.54f,.45f),new Vector3(.045f,.045f,.42f),charcoal).transform.localRotation=Quaternion.Euler(0,0,90);
        Piece("Left handle grip",PrimitiveType.Sphere,player,new Vector3(-.34f,.55f,.43f),new Vector3(.10f,.08f,.09f),orange);
        Piece("Right handle grip",PrimitiveType.Sphere,player,new Vector3(.34f,.55f,.43f),new Vector3(.10f,.08f,.09f),orange);
        Piece("Rear jet nozzle",PrimitiveType.Cylinder,player,new Vector3(0,-.12f,-1.04f),new Vector3(.22f,.22f,.10f),cyan).transform.localRotation=Quaternion.Euler(90,0,0);
        for(int i=0;i<16;i++)
        {
            float x=Random.Range(-.78f,.78f), z=-1.2f-i*.3f;
            var spray=CreatePrimitive("Jetski spray",PrimitiveType.Sphere,movingWorld,new Vector3(x,-.15f,z),new Vector3(Random.Range(.11f,.28f),.09f,Random.Range(.22f,.48f)),foam);
            spray.transform.rotation=Quaternion.Euler(0,Random.Range(-18,18),0);
        }
    }

    void CreateHullMesh(string objectName,Transform parent,Material material)
    {
        float[] z={-1.18f,-.88f,-.42f,.25f,.78f,1.12f};
        float[] w={.42f,.61f,.64f,.55f,.42f,.18f};
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        for(int i=0;i<z.Length;i++)
        {
            float y=i<2?-.08f:-.12f, bottom=-.27f;
            vertices.Add(new Vector3(-w[i],y,z[i])); vertices.Add(new Vector3(-w[i]*.83f,bottom,z[i]));
            vertices.Add(new Vector3(0,bottom-.025f,z[i])); vertices.Add(new Vector3(w[i]*.83f,bottom,z[i]));
            vertices.Add(new Vector3(w[i],y,z[i]));
        }
        for(int i=0;i<z.Length-1;i++) for(int side=0;side<4;side++)
        {
            int a=i*5+side,b=(i+1)*5+side,c=(i+1)*5+side+1,d=i*5+side+1;
            triangles.Add(a);triangles.Add(b);triangles.Add(c); triangles.Add(a);triangles.Add(c);triangles.Add(d);
        }
        var mesh=new Mesh{name="Jetski angular hull mesh"}; mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(objectName);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }

    void CreateDeckMesh(string objectName,Transform parent,Material material,float from=.72f,float to=-.92f,float widthScale=.82f)
    {
        var verts=new[]{new Vector3(-.43f,.055f,from),new Vector3(.43f,.055f,from),new Vector3(.55f,.055f,.10f),new Vector3(.46f,.055f,to),new Vector3(-.46f,.055f,to),new Vector3(-.55f,.055f,.10f)};
        for(int i=0;i<verts.Length;i++) verts[i]=new Vector3(verts[i].x*widthScale,verts[i].y,verts[i].z);
        var mesh=new Mesh{name=objectName+" mesh"};mesh.vertices=verts;mesh.triangles=new[]{0,2,1,0,3,2,0,4,3,0,5,4};mesh.RecalculateNormals();
        var go=new GameObject(objectName);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }

    void CreateLimb(string name,Transform parent,Vector3 start,Vector3 end,Material material,float radius)
    {
        Vector3 delta=end-start;var limb=Piece(name,PrimitiveType.Capsule,parent,(start+end)*.5f,new Vector3(radius,delta.magnitude*.5f+radius,radius),material);
        limb.transform.localRotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
    }

    // Low chase camera like the Blender gameplay preview: rider in the bottom third, horizon about a third down.
    static readonly Vector3 CameraOffset=new Vector3(0,3.7f,-7.9f);
    void CreateCamera()
    {
        bool ice=environment==EnvironmentPreset.IceCave;
        gameCamera=new GameObject("Portrait chase camera").AddComponent<Camera>();
        gameCamera.tag="MainCamera"; gameCamera.fieldOfView=ice?56:50; gameCamera.nearClipPlane=.1f; gameCamera.farClipPlane=ice?220:620;
        gameCamera.backgroundColor=ice ? new Color(.015f,.15f,.21f) : new Color(.8f,.88f,.93f); gameCamera.clearFlags=CameraClearFlags.SolidColor;
        gameCamera.transform.position=CameraOffset; gameCamera.transform.rotation=Quaternion.Euler(ice?13:10,0,0);
        gameCamera.aspect=9f/16f;
        gameCamera.gameObject.AddComponent<AudioListener>();
        var skyShaderToUse=skyShader?skyShader:Shader.Find("JetskiRunner/SkyGradient");
        if(!ice && skyShaderToUse)
        {
            var dome=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name="Sky dome"; Destroy(dome.GetComponent<Collider>());
            dome.transform.SetParent(gameCamera.transform,false);
            dome.transform.localScale=Vector3.one*1100f;
            var r=dome.GetComponent<Renderer>(); r.sharedMaterial=new Material(skyShaderToUse); r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
        }
    }

    void FollowCamera()
    {
        var target=CameraOffset+new Vector3(player.position.x*.78f,JumpOffset*.35f,0);
        gameCamera.transform.position=Vector3.Lerp(gameCamera.transform.position,target,1f-Mathf.Exp(-7f*Time.deltaTime));
        gameCamera.fieldOfView=Mathf.Lerp(gameCamera.fieldOfView,(environment==EnvironmentPreset.IceCave?56:50)+(boostLeft>0?7:0),Time.deltaTime*3f);
    }

    // White spray fans off both sides of the hull plus a rooster tail, as in the gameplay render.
    void CreateSpray()
    {
        if(!sprayMaterial) return;
        sprays.Add(MakeSpray("Left spray fan",new Vector3(-.55f,-.25f,-.2f),new Vector3(-1f,.42f,-.55f),200,6.5f,.32f));
        sprays.Add(MakeSpray("Right spray fan",new Vector3(.55f,-.25f,-.2f),new Vector3(1f,.42f,-.55f),200,6.5f,.32f));
        sprays.Add(MakeSpray("Rooster tail",new Vector3(0,-.2f,-1.3f),new Vector3(0,.55f,-1f),140,4.5f,.38f));
    }
    ParticleSystem MakeSpray(string name,Vector3 local,Vector3 direction,float rate,float speed,float size)
    {
        var go=new GameObject(name); go.transform.SetParent(player,false);
        go.transform.localPosition=local; go.transform.localRotation=Quaternion.LookRotation(direction.normalized);
        var ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.8f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.6f,speed);
        main.startSize=new ParticleSystem.MinMaxCurve(size*.35f,size);
        main.startColor=new Color(1,1,1,.85f);
        main.gravityModifier=1.4f;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=900;
        var emission=ps.emission; emission.rateOverTime=rate;
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=14; shape.radius=.12f;
        var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(0); velocity.y=new ParticleSystem.MinMaxCurve(0); velocity.z=new ParticleSystem.MinMaxCurve(-forwardSpeed);
        var sizeLife=ps.sizeOverLifetime; sizeLife.enabled=true; sizeLife.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.6f,1,1.6f));
        var fade=ps.colorOverLifetime; fade.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(.6f,.5f),new GradientAlphaKey(0,1)});
        fade.color=gradient;
        var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=sprayMaterial;
        ps.Play();
        return ps;
    }

    void CreateShore()
    {
        scenery=new TropicalScenery(Make,sideRockPrefab,dockBarrelPrefab,environment==EnvironmentPreset.PirateCove);
        scenery.BuildBackdrop();
        SpawnShoreUpTo(ShoreAhead);
    }

    const float ShoreAhead=190f;
    void SpawnShoreUpTo(float limit)
    {
        if(scenery==null) return;
        for(int s=0;s<2;s++)
            while(nextShoreAt[s]<limit)
            {
                spawned.Add(scenery.SpawnShore(movingWorld,s==0?-1:1,nextShoreAt[s]));
                nextShoreAt[s]+=Random.Range(17f,27f);
            }
    }

    // Pirate ships work like lane-blocking traffic: some lie at anchor, others sail head-on down a lane.
    void CreateShipRow(int freeLane,float z)
    {
        int movingLane=distanceTravelled>120f && Random.value<.55f ? (freeLane+Random.Range(1,3))%3 : -1;
        for(int laneIndex=0;laneIndex<3;laneIndex++)
        {
            if(laneIndex==freeLane) continue;
            if(laneIndex==movingLane){ SpawnShip(laneIndex,z+Random.Range(10f,25f),true); continue; }
            if(Random.value<.35f) continue;
            int convoy=Random.value<.4f?2:1;
            for(int i=0;i<convoy;i++) SpawnShip(laneIndex,z+i*shipLength,false);
        }
        CreateCoinRow(freeLane,z-2f,4);
    }

    void SpawnShip(int laneIndex,float z,bool sailing)
    {
        if(sailing)
        {
            // keep its lane clear of anything already spawned between the player and the ship
            float x=(laneIndex-1)*laneWidth;
            foreach(var go in spawned)
                if(go && go.GetComponent<RunnerTrigger>() && Mathf.Abs(go.transform.localPosition.x-x)<laneWidth*.5f && go.transform.position.z>18f && go.transform.localPosition.z<z+shipLength)
                    Destroy(go);
        }
        var ship=SpawnAsset(pirateShipPrefab,laneIndex,z,TriggerKind.Ship);
        ship.name=sailing?"Pirate ship | sailing head-on":"Pirate ship | at anchor";
        bool facePlayer=sailing||Random.value<.5f;
        ship.transform.localRotation=Quaternion.Euler(0,facePlayer?180:0,0);
        ship.GetComponent<RunnerTrigger>().SailSpeed=sailing?shipSailSpeed:0f;
    }

    void CreateIceCave()
    {
        if (iceTunnelPrefab)
        {
            for(int i=0;i<8;i++)
            {
                var segment=Instantiate(iceTunnelPrefab,movingWorld).transform;
                segment.name="Ice tunnel segment "+i;
                segment.localPosition=new Vector3(0,WaterY,i*iceTunnelLength);
                iceSegments.Add(segment);
            }
            return;
        }
        var iceMaterial=Make("Faceted blue ice",new Color(.08f,.53f,.68f),.16f,.68f);
        for(int i=0;i<13;i++)
        {
            float z=7+i*9f;
            for(int side=-1;side<=1;side+=2)
            {
                var wall=CreatePrimitive("Ice tunnel wall",PrimitiveType.Cube,movingWorld,new Vector3(side*6.2f,2.8f,z),new Vector3(2.8f,6.5f,10f),iceMaterial);
                wall.transform.rotation=Quaternion.Euler(Random.Range(-10,10),Random.Range(-12,12),Random.Range(-8,8));
                var spike=CreatePrimitive("Hanging ice",PrimitiveType.Cylinder,movingWorld,new Vector3(side*Random.Range(1.8f,4.2f),5.7f,z+Random.Range(-3,3)),new Vector3(.8f,2.4f,.8f),iceMaterial);
                spike.transform.rotation=Quaternion.Euler(180,0,Random.Range(-18,18));
            }
            CreatePrimitive("Ice tunnel arch",PrimitiveType.Cube,movingWorld,new Vector3(0,7f,z),new Vector3(12.5f,1.6f,10f),iceMaterial);
        }
    }

    void CreateCoinRow(int laneIndex,float z,int count)
    {
        for(int i=0;i<count;i++)
        {
            GameObject coin;
            if (goldPrefab)
            {
                coin=Instantiate(goldPrefab,movingWorld);
                coin.name="Gold pickup | Blender anchor coin";
                coin.transform.localPosition=new Vector3((laneIndex-1)*laneWidth,.60f,z+i*3.6f);
                coin.transform.localRotation=Quaternion.Euler(0,0,0);
            }
            else
            {
                coin=CreatePrimitive("Gold pickup",PrimitiveType.Cylinder,movingWorld,new Vector3((laneIndex-1)*laneWidth,.48f,z+i*2.25f),new Vector3(.68f,.14f,.68f),gold);
                coin.transform.rotation=Quaternion.Euler(90,0,0);
            }
            coin.AddComponent<SphereCollider>().isTrigger=true;
            coin.AddComponent<RunnerTrigger>().Init(this,TriggerKind.Coin);
            spawned.Add(coin);
        }
    }
    void CreateZigzagRow(int start,float z)
    { for(int i=0;i<5;i++) CreateCoinRow((start+i)%3,z+i*1.8f,1); }

    GameObject SpawnAsset(GameObject prefab,int laneIndex,float z,TriggerKind kind)
    {
        var go=Instantiate(prefab,movingWorld);
        go.transform.localPosition=new Vector3((laneIndex-1)*laneWidth,WaterY,z);
        go.transform.localRotation=Quaternion.identity;
        AddBoundsTrigger(go,kind>=TriggerKind.Obstacle ? .75f : 1f);
        go.AddComponent<RunnerTrigger>().Init(this,kind);
        spawned.Add(go);
        return go;
    }

    // Box trigger fitted to the visible mesh, shrunk a little for obstacles so near-misses feel fair.
    static void AddBoundsTrigger(GameObject go,float shrink)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();
        if(renderers.Length==0){ go.AddComponent<SphereCollider>().isTrigger=true; return; }
        var bounds=renderers[0].bounds;
        foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        var box=go.AddComponent<BoxCollider>(); box.isTrigger=true;
        box.center=go.transform.InverseTransformPoint(bounds.center);
        var scale=go.transform.lossyScale;
        box.size=new Vector3(bounds.size.x/scale.x*shrink,bounds.size.y/scale.y,bounds.size.z/scale.z*shrink);
    }

    void CreateObstacleRow(int freeLane,float z)
    {
        for(int laneIndex=0;laneIndex<3;laneIndex++)
        {
            if(laneIndex==freeLane || Random.value<.35f) continue;
            var prefab=obstaclePrefabs[Random.Range(0,obstaclePrefabs.Length)];
            SpawnAsset(prefab,laneIndex,z+Random.Range(-1.5f,1.5f),TriggerKind.Obstacle).transform.localRotation=Quaternion.Euler(0,Random.Range(-20f,20f),0);
        }
        CreateCoinRow(freeLane,z-3f,3);
    }

    void CreateRampRow(int laneIndex,float z)
    {
        SpawnAsset(rampPrefab,laneIndex,z,TriggerKind.Ramp);
        // Coins follow the jump arc so a ramp is always worth taking.
        float flight=CurrentSpeed*jumpDuration;
        for(int i=1;i<=4;i++)
        {
            float t=i/5f;
            CreateCoinRow(laneIndex,z+1.5f+t*flight,1);
            var coin=spawned[spawned.Count-1].transform;
            coin.localPosition=new Vector3(coin.localPosition.x,.6f+4f*jumpHeight*t*(1f-t),z+1.5f+t*flight);
        }
        int other=(laneIndex+Random.Range(1,3))%3;
        SpawnAsset(obstaclePrefabs[Random.Range(0,obstaclePrefabs.Length)],other,z+flight*.5f,TriggerKind.Obstacle);
    }

    void CreateFuelPickup(int laneIndex,float z)
    {
        var can=SpawnAsset(fuelPrefab,laneIndex,z,TriggerKind.Fuel);
        can.transform.localPosition+=Vector3.up*.75f;
    }

    void CleanupPassed()
    {
        for(int i=spawned.Count-1;i>=0;i--)
        {
            var go=spawned[i];
            if(go==null){ spawned.RemoveAt(i); continue; }
            if(go.transform.position.z<-30f){ Destroy(go); spawned.RemoveAt(i); }
        }
    }

    void RecycleIceTunnel()
    {
        foreach(var segment in iceSegments)
            if(segment.position.z < -iceTunnelLength*1.5f) segment.localPosition+=Vector3.forward*iceTunnelLength*iceSegments.Count;
    }

    GameObject CreatePrimitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false);
        go.transform.localPosition=position; go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material;
        var collider=go.GetComponent<Collider>(); if(collider!=null && name!="Gold pickup") Destroy(collider);
        return go;
    }
    GameObject Piece(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat)
    { return CreatePrimitive(name,type,parent,pos,scale,mat); }

    void RecycleWater()
    {
        foreach(var t in recycledWater) if(t!=null && t.position.z < -16f) t.position += Vector3.forward*160f;
    }
    public void Collect(GameObject pickup) { coins++; Destroy(pickup); }

    void OnTrigger(RunnerTrigger trigger)
    {
        if(crashed) return;
        switch(trigger.Kind)
        {
            case TriggerKind.Coin: Collect(trigger.gameObject); break;
            case TriggerKind.Fuel: boostLeft=boostDuration; Destroy(trigger.gameObject); break;
            case TriggerKind.Ramp: if(jumpTime<0){ jumpTime=0f; landingCued=false; if(rider) rider.Jump(); } break;
            case TriggerKind.Obstacle:
                if(JumpOffset>.7f) return;           // cleared it in the air
                if(boostLeft>0){ Destroy(trigger.gameObject); if(rider) rider.Hit(); return; } // boost smashes through
                crashed=true;
                if(rider) rider.Fall();
                break;
            case TriggerKind.Ship: crashed=true; if(rider) rider.Fall(); break; // too big to jump or smash: change lanes
        }
    }

    void OnGUI()
    {
        if(distanceStyle==null)
        {
            distanceStyle=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(Screen.height*.052f),fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            distanceStyle.normal.textColor=Color.white;
            coinStyle=new GUIStyle(distanceStyle){fontSize=Mathf.RoundToInt(Screen.height*.032f),alignment=TextAnchor.MiddleRight};
            hintStyle=new GUIStyle(distanceStyle){fontSize=Mathf.RoundToInt(Screen.height*.02f)};
            boostStyle=new GUIStyle(distanceStyle){fontSize=Mathf.RoundToInt(Screen.height*.03f)};
            boostStyle.normal.textColor=new Color(1f,.55f,.2f);
            shadowStyle=new GUIStyle(distanceStyle); shadowStyle.normal.textColor=new Color(0,0,0,.85f);
        }
        float u=Screen.height*.01f;
        var panel=new Color(.02f,.06f,.11f,.92f);
        var pauseRect=new Rect(u*2.4f,u*3.2f,u*7f,u*7f);
        Fill(pauseRect,panel);
        Fill(new Rect(pauseRect.x+u*1.9f,pauseRect.y+u*1.6f,u*1.2f,u*3.8f),Color.white);
        Fill(new Rect(pauseRect.x+u*3.9f,pauseRect.y+u*1.6f,u*1.2f,u*3.8f),Color.white);
        if(!crashed && Event.current.type==EventType.MouseDown && pauseRect.Contains(Event.current.mousePosition)) { Time.timeScale=Time.timeScale>0?0:1; Event.current.Use(); }
        // distance with a hard drop shadow
        var distanceRect=new Rect(Screen.width*.2f,u*3.2f,Screen.width*.6f,u*7f);
        string distance=Mathf.FloorToInt(distanceTravelled).ToString("N0")+" m";
        GUI.Label(new Rect(distanceRect.x+u*.35f,distanceRect.y+u*.45f,distanceRect.width,distanceRect.height),distance,shadowStyle);
        GUI.Label(distanceRect,distance,distanceStyle);
        // coin badge
        var coinRect=new Rect(Screen.width-u*14.5f,u*4.2f,u*12f,u*5f);
        Fill(coinRect,panel);
        Fill(new Rect(coinRect.x-u*1.2f,coinRect.y-u*.6f,u*6.2f,u*6.2f),new Color(.98f,.73f,.16f),true);
        GUI.Label(new Rect(coinRect.x,coinRect.y,coinRect.width-u*1.2f,coinRect.height),coins.ToString("000"),coinStyle);
        if(Time.timeScale==0) GUI.Label(new Rect(0,Screen.height*.42f,Screen.width,Screen.height*.08f),"PAUSED",distanceStyle);
        if(boostLeft>0) GUI.Label(new Rect(0,Screen.height*.12f,Screen.width,Screen.height*.05f),"BOOST!  "+boostLeft.ToString("0.0")+"s",boostStyle);
        if(crashed)
        {
            GUI.Label(new Rect(0,Screen.height*.38f,Screen.width,Screen.height*.08f),"CRASHED!",distanceStyle);
            GUI.Label(new Rect(0,Screen.height*.47f,Screen.width,Screen.height*.05f),"TAP OR PRESS SPACE TO RESTART",hintStyle);
        }
        else if(distanceTravelled<40f) GUI.Label(new Rect(0,Screen.height*.91f,Screen.width,Screen.height*.045f),"SWIPE TO CHANGE LANES",hintStyle);
    }

    static Texture2D circleTexture;
    static void Fill(Rect rect,Color color,bool circle=false)
    {
        if(circle && circleTexture==null)
        {
            circleTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32));
                float shade=d<22?1f:.8f;
                circleTexture.SetPixel(x,y,new Color(shade,shade*.95f,shade*.85f,Mathf.Clamp01(31.5f-d)));
            }
            circleTexture.Apply();
        }
        var previous=GUI.color; GUI.color=color;
        GUI.DrawTexture(rect,circle?circleTexture:Texture2D.whiteTexture);
        GUI.color=previous;
    }

    enum TriggerKind { Coin, Fuel, Ramp, Obstacle, Ship }

    sealed class RunnerTrigger:MonoBehaviour
    {
        JetskiRunnerGame game;
        public TriggerKind Kind { get; private set; }
        public void Init(JetskiRunnerGame owner,TriggerKind kind){ game=owner; Kind=kind; }
        public float SailSpeed { get; set; }
        void Update()
        {
            if(Kind==TriggerKind.Coin) transform.Rotate(0,32f*Time.deltaTime,0,Space.World);
            else if(Kind==TriggerKind.Fuel) transform.Rotate(0,60f*Time.deltaTime,0,Space.World);
            else if(Kind==TriggerKind.Ship && !game.crashed)
            {
                var p=transform.localPosition;
                p.z-=SailSpeed*Time.deltaTime;
                p.y=WaterY+Mathf.Sin(Time.time*1.3f+p.z)*.12f;
                transform.localPosition=p;
                float yaw=transform.localEulerAngles.y;
                transform.localRotation=Quaternion.Euler(Mathf.Sin(Time.time*1.1f+p.z)*1.5f,yaw,Mathf.Sin(Time.time*.9f+p.x)*2.5f);
            }
        }
        void OnTriggerEnter(Collider other)
        { if(other.transform==game.player||other.transform.IsChildOf(game.player)) game.OnTrigger(this); }
    }
}
