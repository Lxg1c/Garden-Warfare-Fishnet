using UnityEngine;

public class QuitGame : MonoBehaviour
{
    public void OnQuitGame()
    {
        #if UNITY_STANDALONE
                Application.Quit();
        #endif
                
        #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}
