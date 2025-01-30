﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

namespace MSCLoader;

internal class ConsoleView : MonoBehaviour
{
    internal ConsoleController controller;
    public GameObject viewContainer; //Container for console view, should be a child of this GameObject
    public Text logTextArea;
    public InputField inputField;
    private bool wasFocused;
    private int commands, pos;
#if !Mini
    void Awake()
    {
        controller = new ConsoleController();
        controller.logChanged += OnLogChanged;
        UpdateLogStr(controller.log);
        SetVisibility(false);
    }

    void OnDestroy()
    {
        controller.logChanged -= OnLogChanged;
    }

    public void ToggleVisibility()
    {
        if (viewContainer.activeSelf)
            viewContainer.transform.GetChild(5).gameObject.GetComponent<ConsoleUIResizer>().SaveConsoleSize();
        SetVisibility(!viewContainer.activeSelf);
        inputField.text = string.Empty;
        if (viewContainer.activeSelf && ModConsole.typing.GetValue())
        {
            inputField.ActivateInputField();
            inputField.Select();
        }
    }

    public void SetVisibility(bool visible)
    {
        viewContainer.SetActive(visible);
    }

    void OnLogChanged(string[] newLog)
    {
        UpdateLogStr(newLog);
    }

    void UpdateLogStr(string[] newLog)
    {
        if (newLog == null)
        {
            logTextArea.text = "";
        }
        else
        {
            logTextArea.text = string.Join(Environment.NewLine, newLog);
        }
    }
#endif
    // Event that should be called by anything wanting to submit the current input to the console.
    public void RunCommand()
    {
#if !Mini
        controller.RunCommandString(inputField.text);
        inputField.text = string.Empty;
        //keep active input field
        inputField.ActivateInputField();
        inputField.Select();
#endif
    }
#if !Mini
    private void FixedUpdate()
    {
        if (inputField.text.Length > 0 && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)))
        {
            RunCommand();
        }
    }
    private void Update()
    {
        if (inputField.isFocused)
        {

            if (!wasFocused)
            {
                wasFocused = true;
                commands = controller.commandHistory.Count;
                pos = commands;
            }
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                //if any command was entered before
                if (commands != 0)
                {
                    if (pos != 0)
                        pos--;
                    inputField.text = controller.commandHistory[pos];
                    inputField.MoveTextEnd(false);
                }
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (commands != 0)
                {
                    pos++;
                    if (pos != commands)
                    {
                        inputField.text = controller.commandHistory[pos];
                        inputField.MoveTextEnd(false);
                    }
                    else
                    {
                        pos--;
                    }

                }
            }
            if (inputField.text.Length > 0 && Input.GetKey(KeyCode.RightArrow))
            {
                //Autocomplete command name
                List<string> found = controller.commands.Keys.Where(w => w.StartsWith(inputField.text)).ToList();
                if (found.Count > 0)
                {
                    inputField.text = found[0];
                    inputField.MoveTextEnd(false);
                }
            }
        }
        else
        {
            wasFocused = false;
        }
    }
#endif
}
