using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class CityBuilderGame : MonoBehaviour
{
    public static CityBuilderGame Instance { get; private set; }

    float gridSize = 1f;          // models snap to a 1-metre grid so they line up
    float cameraMoveSpeed = 20f;  // wasd camera speed
    float cameraZoomSpeed = 30f;  // mousezoom speed

    // add a line here using file name from that folder Assets/Resources/Kaykit folder
    // (for example "watertower", "car_taxi", "road_corner", "streetlight")
    (string label, string file)[] catalog = new (string, string)[]
    {
        ("House",  "building_A"),
        ("Shop",   "building_B"),
        ("Office", "building_C"),
        ("Flats",  "building_D"),
        ("Tower",  "building_E"),
        ("Road",   "road_straight"),
        ("Curve",  "road_corner_curved"),
        ("Corner", "road_corner"),
        ("Cross",  "road_straight_crossing"),
        ("T-Road", "road_tsplit"),
        ("4-Way",  "road_junction"),
        ("Tree",   "bush"),
        ("Car",    "car_sedan"),
    };

    // things the game remembers while it runs
    // "public" makes a slot appear on the script
    public GameObject ground;
    // List<GameObject> is a growable collection of GameObjects. Starts empty ("new List...").
    List<GameObject> buildingTypes = new List<GameObject>();  // one hidden template per menu item
    GameObject selected;                                      // the model which we will place
    GameObject ghost;                                         // the preview of obj that follows the mouse
    float rotationY = 0f;                                     // how far the next building is turned
    bool deleteMode = false;                                  // true after clicking the Delete button
    int selectedIndex = 0;                                    // which menu item is picked (used for saving)
    bool paused = false;                                      // true while the Esc menu is open

    public GameObject pauseMenu;  // the Esc menu panel which starts hidden
    public Text savedLabel;       // the "last saved" text near the Save button

    public AudioClip placeSound;  // soundfile for placing buildings
    AudioSource sfxSource;        // speaker that plays placeSound

    public GameObject dustPuff;   // DustPuff

    // Awake() runs once, before Start(). We set up the singleton here so the
    // single instance is ready as early as possible.
    void Awake()
    {
        // If second CityBuilderGame exists, it destroys so only one is left
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this; // object becomes the one and only instance
    }

    // Start() runs once when pressed Play

    void Start()
    {
        // "Ground" is the green platform in the game
        if (ground == null) ground = GameObject.Find("Ground");
        if (pauseMenu != null) pauseMenu.SetActive(false); // make sure the Esc menu starts hidden

        // a speaker on this object, just for sound effects & separate from bgm
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        BuildTemplates();
        LoadCity(); // bring back a city saved before
        if (buildingTypes.Count > 0) selected = buildingTypes[0]; // start with the first item in list
    }
    // Update() runs automatically every frame while playing.
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) TogglePause(); // open/close the Esc menu
        if (paused) return; // while paused, ignore building and camera controls

        MoveCamera();
        if (Input.GetKeyDown(KeyCode.R)) Rotate(); // press R to rotate the building
        PlaceBuildings();
    }

    // Turn the next building a quarter-turn (90 degrees). With the R key
    public void Rotate()
    {
        rotationY += 90f;
    }

    // loading the KayKit assets models

    void BuildTemplates()
    {
        foreach (var entry in catalog)
        {
            GameObject template = MakeTemplate(entry.file);
            template.SetActive(false);
            buildingTypes.Add(template);
        }
    }

    // Load KayKit model from Resources, wrap it in a parent that has a
    // click-collider sized to fit the model
    GameObject MakeTemplate(string file)
    {
        GameObject wrapper = new GameObject(file);

        // Resources.Load finds the file in any folder called "Resources"
        GameObject model = Resources.Load<GameObject>("KayKit/" + file);
        if (model == null) return wrapper; // file name not found in Resources/KayKit - skip it

        GameObject instance = Instantiate(model, wrapper.transform);
        instance.transform.localPosition = Vector3.zero;

        // Add a box collider shaped like the obj/model so the Delete button
        // can tell which building I clicked on
        Bounds b = GetBounds(wrapper);
        BoxCollider col = wrapper.AddComponent<BoxCollider>();
        col.center = b.center;         // wrapper sits at (0,0,0), so world = local here
        col.size = b.size;

        return wrapper;
    }

    // Measure how big a model is by combining all its visible parts.
    Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // placing buildings 

    void PlaceBuildings()
    {
        // MainCamera
        if (Camera.main == null) return;

        // Ignore clicks that are over a menu button or the legend so it doesn't drop a building behind
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (ghost != null) ghost.SetActive(false); // Hide the preview of obj while over the UI
            return;
        }
    // draws an invisible line from the camera out through wherever the mouse is pointing, into the 3D world 
    // It’s stored in ray so the next lines can check what that line hits (like the ground)
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // delete mode: click any building (anything that isn't the ground) to remove it.
        if (deleteMode)
        {
            if (ghost != null) ghost.SetActive(false); // no preview in delete mode
            RaycastHit hit; // a box to receive info about what selected
            // conditions to delete: left mouse pressed, ray hit something within 1000m, the click was not the ground
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(ray, out hit, 1000f)
                && hit.collider.gameObject != ground)
            {
                Destroy(hit.collider.gameObject);
            }
            return;
        }

        // Build mode: work out the spot on the flat ground (y = 0) right under the
        // mouse. Using the ground plane directly, so other buildings never get in
        // the way I can place pieces right next to or on top of each other.
        if (selected == null) return; // does nothing if no object chosen
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero); 
        float enter;
        if (groundPlane.Raycast(ray, out enter))
        {
            Vector3 point = SnapToGrid(ray.GetPoint(enter));
            ShowGhost(point);
            
            // if left mouse button was clicked this frame, place something
            if (Input.GetMouseButtonDown(0))
            {
                GameObject placed = Instantiate(selected, point, Quaternion.Euler(0f, rotationY, 0f));
                placed.SetActive(true);
                placed.AddComponent<PlacedBuilding>().typeIndex = selectedIndex;
                // adds "PlacedBuilding" tag to remember type for saving later

                if (placeSound != null) sfxSource.PlayOneShot(placeSound); // plop sound effect
                if (dustPuff != null) Instantiate(dustPuff, point, Quaternion.identity); // dust VFX
            }
        }
    }

    Vector3 SnapToGrid(Vector3 position)
    {
        float x = Mathf.Round(position.x / gridSize) * gridSize;
        float z = Mathf.Round(position.z / gridSize) * gridSize;
        return new Vector3(x, 0f, z);
    }

// Creates the transparent preview object that follows the mouse
    void ShowGhost(Vector3 position)
    {
        // If the game don't have a ghost yet or the ghost is for a different model than selected,
        // it needs to (re)build it
        if (ghost == null || ghost.name != "GHOST_" + selected.name)
        {
            if (ghost != null) Destroy(ghost); // throw away old preview if there was one
            ghost = Instantiate(selected); // make new copy of selected model
            ghost.name = "GHOST_" + selected.name; // names it so it can be recognised next frame
            // remove colliders so the preview never blocks the mouse, "true" to include inactive children too
            foreach (Collider c in ghost.GetComponentsInChildren<Collider>(true)) Destroy(c);
        }
        ghost.SetActive(true);
        ghost.transform.position = position;
        ghost.transform.rotation = Quaternion.Euler(0f, rotationY, 0f); // preview shows the turn
    }

    // buttons

    // Each toolbar button calls this with its own number. The numbers match the
    // "catalog" list above: 0 = House, 1 = Shop, 2 = Office, 3 = Flats, 4 = Tower,
    // 5 = Road, 6 = Curve, 7 = Corner, 8 = Cross, 9 = T-Road, 10 = 4-Way,
    // 11 = Tree, 12 = Car etc.
    public void Select(int index)
    {
        deleteMode = false;     // choosing building cancels delete mode
        selectedIndex = index; // remember which menu number was chosen
        selected = buildingTypes[index]; // point "selected" at the template so it can copies it
    }

    // The Delete button mode. Click a building to remove it.
    public void DeleteMode()
    {
        deleteMode = true;     // Turn delete mode on
        selected = null;       // Nothing selected for building right now
        if (ghost != null) ghost.SetActive(false); // hide preview while deleting mode
    }

    // camera movement 

    void MoveCamera()
    {
        Camera cam = Camera.main; // grabs main cam
        if (cam == null) return; // if no cam, nothing to move
        // GetAxis("Horizontal") returns -1..1 from A/D (and left/right arrows)
        // GetAxis("Vertical") returns -1..1 from W/S (and up/down arrows)
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        // Move the camera by that direction, scaled by speed
        // Time.deltaTime = seconds since last frame; multiplying by it makes movement
        // smooth and the same speed on fast and slow computers
        cam.transform.position += new Vector3(x, 0f, z) * cameraMoveSpeed * Time.deltaTime;
        // Mouse ScrollWheel is positive when scrolling up. Then subtract it from the cameras
        // height (y) so scrolling up moves the camera down = zooms in
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        cam.transform.position += new Vector3(0f, -scroll * cameraZoomSpeed, 0f);
    }

    // saving & loading 

    // Save every placed building (type, position, rotation) to disk, and
    // remember the current date/time
    public void SaveCity()
    {
        SaveData data = new SaveData();
        // Find every PlacedBuilding currently in the scene and loop over them
        // (FindObjectsInactive.Exclude = skip hidden ones; SortMode.None = don't bother sorting = faster.)
        foreach (PlacedBuilding pb in FindObjectsByType<PlacedBuilding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            SaveItem item = new SaveItem(); // One record per building
            item.type = pb.typeIndex; // Which menu item this building was
            Vector3 p = pb.transform.position; // Where it sits in the world
            item.x = p.x; item.y = p.y; item.z = p.z; // Store each coordinate separately
            item.rotY = pb.transform.eulerAngles.y; // Save its turn (rotation around up-axis)
            data.items.Add(item); // add this record to the saving list
        }

        // PlayerPrefs is Unity's simple built-in storage. JsonUtility turns the list into text so it can be stored
        PlayerPrefs.SetString("citySave", JsonUtility.ToJson(data));

        // Build a readable timestamp like e.g "09 Nov 2026  14:30"
        string now = System.DateTime.Now.ToString("dd MMM yyyy  HH:mm");
        PlayerPrefs.SetString("citySaveTime", now); // save the timestamp
        PlayerPrefs.Save(); // writes everything to the disk

        // Update the on-screen "Last saved" label
        if (savedLabel != null) savedLabel.text = "Last saved: " + now;
    }

    // Load a saved city (if one exists) and rebuild all the buildings which is called in Start()
    void LoadCity()
    {
        // HasKey checks if we saved before. If no, nothing to load
        if (!PlayerPrefs.HasKey("citySave")) return;
        // Read the saved text back and turn it ("FromJson") into a SaveData object
        SaveData data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString("citySave"));
        if (data == null) return; // Safety: if something went wrong, stop

        // recreate each saved building
        foreach (SaveItem item in data.items)
        {
            // Safety check: ignore records whose type number is out of range
            // (e.g. if removed a menu item since saving). "continue" = skip to next loop.
            if (item.type < 0 || item.type >= buildingTypes.Count) continue; // safety check
            // Make a copy of the correct template at the saved position and rotation
            GameObject placed = Instantiate(buildingTypes[item.type],
                new Vector3(item.x, item.y, item.z), Quaternion.Euler(0f, item.rotY, 0f));
            placed.SetActive(true);
            // Re-attach the tag so this rebuilt building can be saved again next time
            placed.AddComponent<PlacedBuilding>().typeIndex = item.type;
        }
        // Restore the "Last saved" label text from the saved timestamp, if exist
        if (savedLabel != null && PlayerPrefs.HasKey("citySaveTime"))
            savedLabel.text = "Last saved: " + PlayerPrefs.GetString("citySaveTime");
    }

    // Esc menu

    // Opens or closes the Esc menu.
    public void TogglePause()
    {
        paused = !paused; // "!" flips a bool. So true becomes false and false becomes true
        if (pauseMenu != null) pauseMenu.SetActive(paused); // Show the panel only while paused.
    }

    // Quits the game and stops Play mode when testing inside the editor
    public void ExitGame()
    {
        Application.Quit(); // In a built game, this closes the application.

        // everything between #if and #endif is only compiled while running inside unity editor,
        // so Application.Quit() does nothing in the editor, we stop the Play mode manually instead
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

}

// A tag put on every placed building so Save knows which menu item it was. A tiny own MonoBehaviour that holds single number
public class PlacedBuilding : MonoBehaviour
{
    public int typeIndex; // 0-12, matching the catalog list from above
}

// These small classes describe what we store when saving. [System.Serializable]
// lets Unity turn them into text.
[System.Serializable]
public class SaveItem
{
    public int type; // which menu item (house,road,..)
    public float x, y, z, rotY; // menu item position (x,y,z) and its turn (rotY)
}

[System.Serializable]
public class SaveData
{
    public List<SaveItem> items = new List<SaveItem>(); // A list of all the SaveItems. One per placed building. Starts empty.
}
