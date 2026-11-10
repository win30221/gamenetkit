using System;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleHud
    {
        [Serializable] private sealed class RoomCopyReply { public string code, status; public int requestId; }
        private int clipboardRequest, clipboardIdentity;
        private string clipboardCode, clipboardStatus;
        private bool clipboardPending;
        private IVisualElementScheduledItem clipboardFeedback;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool clipboardKeyboardCapture;
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RF_Battle_CopyRoomCode(string code, string target, int requestId);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void RF_Battle_CancelRoomCopy(string target);
#endif

        private void CopyRoomCode(string roomCode)
        {
            if (clipboardPending || Flow?.Room?.code != roomCode || string.IsNullOrEmpty(roomCode)) return;
            clipboardCode = roomCode;
            clipboardIdentity = Flow.Identity.Revision;
            clipboardPending = true;
            clipboardStatus = "正在複製…";
            copiedRoomCode = null;
            int request = ++clipboardRequest;
            if (clipboardFeedback == null) clipboardFeedback = root.schedule.Execute(RefreshClipboardFeedback).Every(100);
            else clipboardFeedback.Resume();
            RefreshClipboardFeedback();
#if UNITY_WEBGL && !UNITY_EDITOR
            // The DOM fallback must receive OS copy shortcuts instead of Unity's canvas.
            clipboardKeyboardCapture = WebGLInput.captureAllKeyboardInput;
            WebGLInput.captureAllKeyboardInput = false;
            RF_Battle_CopyRoomCode(roomCode, gameObject.name, request);
#else
            string status = "copied";
            try { GUIUtility.systemCopyBuffer = roomCode; }
            catch (Exception) { status = "failed"; }
            OnBattleRoomCodeCopyResult(JsonUtility.ToJson(new RoomCopyReply { code = roomCode, requestId = request, status = status }));
#endif
        }

        [Preserve]
        public void OnBattleRoomCodeCopyResult(string json)
        {
            RoomCopyReply reply;
            try { reply = JsonUtility.FromJson<RoomCopyReply>(json); }
            catch (Exception) { return; }
            if (reply == null || !clipboardPending || reply.requestId != clipboardRequest || reply.code != clipboardCode) return;
            if (reply.status != "copied" && reply.status != "cancelled" && reply.status != "failed") return;
            clipboardPending = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = clipboardKeyboardCapture;
#endif
            // A browser promise may finish after a logout, room switch or a replacement request.
            if (Flow == null || Flow.Identity.Revision != clipboardIdentity || Flow.Room?.code != clipboardCode)
            {
                clipboardStatus = "";
                clipboardFeedback?.Pause();
                return;
            }
            copiedRoomCode = reply.status == "copied" ? clipboardCode : null;
            clipboardStatus = reply.status == "copied" ? "已複製房間代碼" : reply.status == "cancelled" ? "" : "複製失敗，請重試。";
            RefreshClipboardFeedback();
            MarkDirty();
        }

        private void RefreshClipboardFeedback()
        {
            if (Flow == null || Flow.Identity.Revision != clipboardIdentity || Flow.Room?.code != clipboardCode)
            {
                CancelClipboard();
                return;
            }
            // The menu is rebuilt on room polls. Keep asynchronous feedback on its replacement elements.
            var hint = root?.Q<Label>("roomCopyStatus");
            if (hint != null) { hint.text = clipboardStatus ?? ""; hint.style.display = hint.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
            var button = root?.Q<Button>("roomCopyButton");
            if (button != null) button.SetEnabled(!clipboardPending);
        }

        private void CancelClipboard()
        {
            clipboardFeedback?.Pause();
            if (!clipboardPending) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            RF_Battle_CancelRoomCopy(gameObject.name);
            WebGLInput.captureAllKeyboardInput = clipboardKeyboardCapture;
#endif
            clipboardPending = false;
            ++clipboardRequest;
            clipboardStatus = "";
        }

        private void OnDisable() { CancelClipboard(); if(client!=null)ResetTouchControls(); }
    }
}
