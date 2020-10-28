using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class Main : MonoBehaviour {

	// Use this for initialization
	void Awake () {
		//启动Facade操作
		UnityFacade.GetInstance().StartUp();
		//net启动
		NetWork.GetNetWork().init();
	}
	
	// Update is called once per frame
	public void GotoNextScene () {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
	}
}
