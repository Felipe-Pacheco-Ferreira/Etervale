// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the platform of the browser build (monogame/issues/021-webgpu-backend.md), in place of
// sdl/MGP_sdl.cpp. The window is the page's canvas (MG_WEB_CANVAS); its native handle is the canvas selector, which the
// WebGPU backend turns into a surface. The browser owns the frame loop, so the platform asks for the asynchronous run
// behaviour and the page calls Game.Tick once per animation frame.

#include "api_MGP.h"
#include "mg_common.h"

#include <emscripten/html5.h>

#include <cmath>

// The canvas the page gives the game; one window per page.
#define MG_WEB_CANVAS "#canvas"

struct MGP_Window;

// The character of a key event for a key that types nothing: a noncharacter, so the framework's control-character
// check (Backspace, Tab, Enter, Escape, Delete reach the text input from their key event) passes it over.
#define MG_WEB_NO_CHARACTER 0xFFFFu

// One wheel notch, as the desktop platform reports it.
static const mgint MG_WEB_WHEEL_NOTCH = 120;

struct MGP_Platform
{
	std::vector<MGP_Window*> windows;
	std::queue<MGP_Event> events;
	// The mouse buttons held on the canvas, one bit per MGMouseButton, released when the pointer leaves it.
	mguint heldButtons = 0;
};

struct MGP_Window
{
	MGP_Platform* platform = nullptr;
	std::string title;
	mgint width = 0;
	mgint height = 0;
	mgbool allowResizing = true;
	mgbool borderless = true;
};

struct MGP_Cursor
{
	MGSystemCursor cursor;
};

static void MGP_Web_Push(MGP_Platform* platform, MGEventType type, MGP_Window* window, mgint data1, mgint data2)
{
	MGP_Event event_ = {};
	event_.Type = type;
	event_.Timestamp = (mgulong)emscripten_get_now();
	event_.Window.Window = window;
	event_.Window.Data1 = data1;
	event_.Window.Data2 = data2;
	platform->events.push(event_);
}

// The canvas's drawing size follows its laid-out size in device pixels, so the game renders at the display's density.
static void MGP_Web_FitCanvas(MGP_Window* window, mgbool notify)
{
	double cssWidth = 0;
	double cssHeight = 0;
	if (emscripten_get_element_css_size(MG_WEB_CANVAS, &cssWidth, &cssHeight) != EMSCRIPTEN_RESULT_SUCCESS)
	{
		return;
	}

	double ratio = emscripten_get_device_pixel_ratio();
	mgint width = (mgint)std::lround(cssWidth * ratio);
	mgint height = (mgint)std::lround(cssHeight * ratio);
	if (width <= 0 || height <= 0 || (width == window->width && height == window->height))
	{
		return;
	}

	emscripten_set_canvas_element_size(MG_WEB_CANVAS, width, height);
	window->width = width;
	window->height = height;
	if (notify)
	{
		MGP_Web_Push(window->platform, MGEventType::WindowResized, window, width, height);
	}
}

static EM_BOOL MGP_Web_OnResize(int eventType, const EmscriptenUiEvent* uiEvent, void* userData)
{
	auto platform = static_cast<MGP_Platform*>(userData);
	for (auto window : platform->windows)
	{
		MGP_Web_FitCanvas(window, true);
	}

	return EM_FALSE;
}

static EM_BOOL MGP_Web_OnFocus(int eventType, const EmscriptenFocusEvent* focusEvent, void* userData)
{
	auto platform = static_cast<MGP_Platform*>(userData);
	MGP_Window* window = platform->windows.empty() ? nullptr : platform->windows[0];
	auto type = eventType == EMSCRIPTEN_EVENT_FOCUS ? MGEventType::WindowGainedFocus : MGEventType::WindowLostFocus;
	MGP_Web_Push(platform, type, window, 0, 0);
	return EM_FALSE;
}

static MGP_Window* MGP_Web_FirstWindow(MGP_Platform* platform)
{
	return platform->windows.empty() ? nullptr : platform->windows[0];
}

// The key of a browser key event: its keyCode is the Windows virtual-key code, which the XNA keys share, except for the
// modifiers (one code for both sides, told apart by the location) and the punctuation codes Firefox numbers differently.
static MGKeys MGP_Web_Key(const EmscriptenKeyboardEvent* keyEvent)
{
	bool right = keyEvent->location == DOM_KEY_LOCATION_RIGHT;
	switch (keyEvent->keyCode)
	{
	case 16: return right ? MGKeys::RightShift : MGKeys::LeftShift;
	case 17: return right ? MGKeys::RightControl : MGKeys::LeftControl;
	case 18: return right ? MGKeys::RightAlt : MGKeys::LeftAlt;
	case 59: return MGKeys::OemSemicolon;
	case 61: return MGKeys::OemPlus;
	case 173: return MGKeys::OemMinus;
	default: return keyEvent->keyCode < 256 ? (MGKeys)keyEvent->keyCode : MGKeys::None;
	}
}

// The one character a key event types (its key is "a", "Ç" or " "), or 0 when it types none ("Shift", "ArrowLeft") or
// a shortcut holds Control or Meta; Control with Alt is AltGr, which types.
static mguint MGP_Web_TypedCharacter(const EmscriptenKeyboardEvent* keyEvent)
{
	if (keyEvent->metaKey || (keyEvent->ctrlKey && !keyEvent->altKey))
	{
		return 0;
	}

	auto bytes = reinterpret_cast<const unsigned char*>(keyEvent->key);
	mguint codePoint;
	int length;
	if (bytes[0] < 0x80)
	{
		codePoint = bytes[0];
		length = 1;
	}
	else if ((bytes[0] & 0xE0) == 0xC0)
	{
		codePoint = bytes[0] & 0x1Fu;
		length = 2;
	}
	else if ((bytes[0] & 0xF0) == 0xE0)
	{
		codePoint = bytes[0] & 0x0Fu;
		length = 3;
	}
	else
	{
		return 0;
	}

	for (int i = 1; i < length; i++)
	{
		if ((bytes[i] & 0xC0) != 0x80)
		{
			return 0;
		}

		codePoint = (codePoint << 6) | (bytes[i] & 0x3Fu);
	}

	return bytes[length] == 0 && codePoint >= 0x20 && codePoint != 0x7F ? codePoint : 0;
}

static mguint MGP_Web_ControlCharacter(MGKeys key)
{
	switch (key)
	{
	case MGKeys::Back: return '\b';
	case MGKeys::Tab: return '\t';
	case MGKeys::Enter: return '\r';
	case MGKeys::Escape: return 27;
	case MGKeys::Delete: return 127;
	default: return 0;
	}
}

// Keys reach the canvas while it has the focus (the page gives it a tabindex). The keys the game takes keep their
// browser action from running: typed characters, Tab (focus moves), Backspace, Space (the page scrolls), Enter and the
// arrows; browser shortcuts and the function keys keep theirs.
static EM_BOOL MGP_Web_OnKey(int eventType, const EmscriptenKeyboardEvent* keyEvent, void* userData)
{
	auto platform = static_cast<MGP_Platform*>(userData);
	MGKeys key = MGP_Web_Key(keyEvent);
	if (key == MGKeys::None)
	{
		return EM_FALSE;
	}

	mguint typed = MGP_Web_TypedCharacter(keyEvent);
	mguint control = MGP_Web_ControlCharacter(key);
	MGP_Event event_ = {};
	event_.Type = eventType == EMSCRIPTEN_EVENT_KEYDOWN ? MGEventType::KeyDown : MGEventType::KeyUp;
	event_.Timestamp = (mgulong)emscripten_get_now();
	event_.Key.Window = MGP_Web_FirstWindow(platform);
	event_.Key.Key = key;
	event_.Key.Character = control != 0 ? control : MG_WEB_NO_CHARACTER;
	platform->events.push(event_);
	if (eventType != EMSCRIPTEN_EVENT_KEYDOWN)
	{
		return EM_FALSE;
	}

	if (typed != 0 && typed <= 0xFFFF)
	{
		event_.Type = MGEventType::TextInput;
		event_.Key.Character = typed;
		platform->events.push(event_);
	}

	bool shortcut = keyEvent->metaKey || (keyEvent->ctrlKey && !keyEvent->altKey);
	bool taken = typed != 0 || control != 0 || (key >= MGKeys::Left && key <= MGKeys::Down);
	return taken && !shortcut ? EM_TRUE : EM_FALSE;
}

// Canvas coordinates in CSS pixels to back buffer pixels: the canvas's drawing size is its laid-out size times the
// device pixel ratio (MGP_Web_FitCanvas).
static mgint MGP_Web_ToPixels(long cssCoordinate)
{
	return (mgint)std::lround(cssCoordinate * emscripten_get_device_pixel_ratio());
}

static void MGP_Web_PushButton(MGP_Platform* platform, MGEventType type, MGMouseButton button, mgint x, mgint y)
{
	MGP_Event event_ = {};
	event_.Type = type;
	event_.Timestamp = (mgulong)emscripten_get_now();
	event_.MouseButton.Window = MGP_Web_FirstWindow(platform);
	event_.MouseButton.Button = button;
	event_.MouseButton.X = x;
	event_.MouseButton.Y = y;
	platform->events.push(event_);
}

// The DOM numbers the buttons as MGMouseButton does: main, auxiliary, secondary, back, forward.
static EM_BOOL MGP_Web_OnMouse(int eventType, const EmscriptenMouseEvent* mouseEvent, void* userData)
{
	auto platform = static_cast<MGP_Platform*>(userData);
	mgint x = MGP_Web_ToPixels(mouseEvent->targetX);
	mgint y = MGP_Web_ToPixels(mouseEvent->targetY);
	switch (eventType)
	{
	case EMSCRIPTEN_EVENT_MOUSEMOVE:
	{
		MGP_Event event_ = {};
		event_.Type = MGEventType::MouseMove;
		event_.Timestamp = (mgulong)emscripten_get_now();
		event_.MouseMove.Window = MGP_Web_FirstWindow(platform);
		event_.MouseMove.X = x;
		event_.MouseMove.Y = y;
		platform->events.push(event_);
		return EM_FALSE;
	}
	case EMSCRIPTEN_EVENT_MOUSEDOWN:
	case EMSCRIPTEN_EVENT_MOUSEUP:
	{
		if (mouseEvent->button > (unsigned short)MGMouseButton::X2)
		{
			return EM_FALSE;
		}

		mguint bit = 1u << mouseEvent->button;
		bool down = eventType == EMSCRIPTEN_EVENT_MOUSEDOWN;
		platform->heldButtons = down ? platform->heldButtons | bit : platform->heldButtons & ~bit;
		MGP_Web_PushButton(platform, down ? MGEventType::MouseButtonDown : MGEventType::MouseButtonUp, (MGMouseButton)mouseEvent->button, x, y);
		// The default action of a press focuses the canvas, which its key events need.
		return EM_FALSE;
	}
	case EMSCRIPTEN_EVENT_MOUSELEAVE:
	{
		for (mgint button = 0; button <= (mgint)MGMouseButton::X2; button++)
		{
			if (platform->heldButtons & (1u << button))
			{
				MGP_Web_PushButton(platform, MGEventType::MouseButtonUp, (MGMouseButton)button, x, y);
			}
		}

		platform->heldButtons = 0;
		return EM_FALSE;
	}
	default:
		return EM_FALSE;
	}
}

// Each wheel event is one notch: the browser reports a notch as some pixels or one line, by device. Its positive delta
// scrolls down where the framework's scrolls up.
static EM_BOOL MGP_Web_OnWheel(int eventType, const EmscriptenWheelEvent* wheelEvent, void* userData)
{
	auto platform = static_cast<MGP_Platform*>(userData);
	auto notches = [](double delta) { return delta > 0 ? -1 : delta < 0 ? 1 : 0; };
	MGP_Event event_ = {};
	event_.Type = MGEventType::MouseWheel;
	event_.Timestamp = (mgulong)emscripten_get_now();
	event_.MouseWheel.Window = MGP_Web_FirstWindow(platform);
	event_.MouseWheel.Scroll = notches(wheelEvent->deltaY) * MG_WEB_WHEEL_NOTCH;
	event_.MouseWheel.ScrollH = -notches(wheelEvent->deltaX) * MG_WEB_WHEEL_NOTCH;
	platform->events.push(event_);
	return EM_TRUE;
}

static void MGP_Web_SetInputCallbacks(MGP_Platform* platform)
{
	emscripten_set_keydown_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnKey : nullptr);
	emscripten_set_keyup_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnKey : nullptr);
	emscripten_set_mousedown_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnMouse : nullptr);
	emscripten_set_mouseup_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnMouse : nullptr);
	emscripten_set_mousemove_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnMouse : nullptr);
	emscripten_set_mouseleave_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnMouse : nullptr);
	emscripten_set_wheel_callback(MG_WEB_CANVAS, platform, EM_FALSE, platform != nullptr ? MGP_Web_OnWheel : nullptr);
}

MGP_Platform* MGP_Platform_Create(MGGameRunBehavior& behavior)
{
	behavior = MGGameRunBehavior::Asynchronous;
	auto platform = new MGP_Platform();
	emscripten_set_resize_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, platform, EM_FALSE, MGP_Web_OnResize);
	emscripten_set_focus_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, platform, EM_FALSE, MGP_Web_OnFocus);
	emscripten_set_blur_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, platform, EM_FALSE, MGP_Web_OnFocus);
	MGP_Web_SetInputCallbacks(platform);
	return platform;
}

void MGP_Platform_Destroy(MGP_Platform* platform)
{
	if (platform == nullptr)
	{
		return;
	}

	emscripten_set_resize_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, nullptr, EM_FALSE, nullptr);
	emscripten_set_focus_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, nullptr, EM_FALSE, nullptr);
	emscripten_set_blur_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, nullptr, EM_FALSE, nullptr);
	MGP_Web_SetInputCallbacks(nullptr);
	delete platform;
}

void MGP_Platform_BeforeInitialize(MGP_Platform* platform)
{
}

mgbyte MGP_Platform_PollEvent(MGP_Platform* platform, MGP_Event& event_)
{
	assert(platform != nullptr);
	if (platform->events.empty())
	{
		return 0;
	}

	event_ = platform->events.front();
	platform->events.pop();
	return 1;
}

void MGP_Platform_StartRunLoop(MGP_Platform* platform)
{
}

mgbyte MGP_Platform_BeforeRun(MGP_Platform* platform)
{
	return 1;
}

mgbyte MGP_Platform_BeforeUpdate(MGP_Platform* platform)
{
	return 1;
}

mgbyte MGP_Platform_BeforeDraw(MGP_Platform* platform)
{
	return 1;
}

void* MGP_Platform_MakePath(const char* location, const char* path)
{
	assert(location != nullptr);
	assert(path != nullptr);

	size_t locationLength = strlen(location);
	size_t length = locationLength + strlen(MG_PATH_SEPARATOR) + strlen(path) + 1;
	char* fpath = (char*)malloc(length);
	assert(fpath != nullptr);
	if (locationLength > 0)
	{
		snprintf(fpath, length, "%s%s%s", location, MG_PATH_SEPARATOR, path);
	}
	else
	{
		snprintf(fpath, length, "%s", path);
	}

	return fpath;
}

void MGP_Platform_Free(void* ptr)
{
	free(ptr);
}

MGMonoGamePlatform MGP_Platform_GetPlatform()
{
	return MGMonoGamePlatform::WebGL;
}

MGGraphicsBackend MGP_Platform_GetGraphicsBackend()
{
	return MGGraphicsBackend::WebGPU;
}

MGP_Window* MGP_Window_Create(MGP_Platform* platform, mgint& width, mgint& height, const char* title, const MGP_WindowCreateInfo* windowCreateInfo)
{
	assert(platform != nullptr);
	auto window = new MGP_Window();
	window->platform = platform;
	window->title = title != nullptr ? title : "";
	platform->windows.push_back(window);
	MGP_Web_FitCanvas(window, false);
	if (window->width > 0)
	{
		width = window->width;
		height = window->height;
	}

	return window;
}

mgbyte MGP_Window_CreateNativeWindow(MGP_Window* window, mgint& width, mgint& height, const char* title, const MGP_WindowCreateInfo* windowCreateInfo)
{
	return 1;
}

mgbyte MGP_Window_BeginRecreateNativeWindow(MGP_Window* window, mgint& width, mgint& height, const char* title, const MGP_WindowCreateInfo* windowCreateInfo)
{
	return 1;
}

mgbyte MGP_Window_RecreateForGraphicsRetry(MGP_Window* window)
{
	return 0;
}

void MGP_Window_FinalizeRecreateNativeWindow(MGP_Window* window)
{
}

void MGP_Window_Destroy(MGP_Window* window)
{
	if (window == nullptr)
	{
		return;
	}

	auto& windows = window->platform->windows;
	windows.erase(std::remove(windows.begin(), windows.end(), window), windows.end());
	delete window;
}

void MGP_Window_DestroyNativeWindow(MGP_Window* window)
{
}

void MGP_Window_SetIconBitmap(MGP_Window* window, mgbyte* icon, mgint length)
{
}

void* MGP_Window_GetNativeHandle(MGP_Window* window)
{
	static const char canvas[] = MG_WEB_CANVAS;
	return (void*)canvas;
}

mgbyte MGP_Window_GetAllowUserResizing(MGP_Window* window)
{
	return window->allowResizing ? 1 : 0;
}

void MGP_Window_SetAllowUserResizing(MGP_Window* window, mgbyte allow)
{
	window->allowResizing = allow != 0;
}

mgbyte MGP_Window_GetIsBorderless(MGP_Window* window)
{
	return window->borderless ? 1 : 0;
}

void MGP_Window_SetIsBorderless(MGP_Window* window, mgbyte borderless)
{
	window->borderless = borderless != 0;
}

void MGP_Window_SetTitle(MGP_Window* window, const char* title)
{
	window->title = title != nullptr ? title : "";
}

void MGP_Window_Show(MGP_Window* window, mgbyte show)
{
}

void MGP_Window_GetPosition(MGP_Window* window, mgint& x, mgint& y)
{
	x = 0;
	y = 0;
}

void MGP_Window_SetPosition(MGP_Window* window, mgint x, mgint y)
{
}

// The page lays the canvas out; the game's preferred size does not resize it.
void MGP_Window_SetClientSize(MGP_Window* window, mgint width, mgint height)
{
	MGP_Web_FitCanvas(window, true);
}

void MGP_Window_SetCursor(MGP_Window* window, MGP_Cursor* cursor)
{
}

mgint MGP_Window_ShowMessageBox(MGP_Window* window, const char* title, const char* description, const char* buttons, mgint count)
{
	MG_ERROR_PRINT(description);
	return -1;
}

MG_EXPORT mgbyte MGP_Window_GetIsMaximized(MGP_Window* window)
{
	return 0;
}

MG_EXPORT void MGP_Window_Maximize(MGP_Window* window)
{
}

void MGP_Window_EnterFullScreen(MGP_Window* window, mgbyte useHardwareModeSwitch)
{
}

void MGP_Window_ExitFullScreen(MGP_Window* window)
{
}

void MGP_Mouse_SetVisible(MGP_Platform* platform, mgbyte visible)
{
}

void MGP_Mouse_WarpPosition(MGP_Window* window, mgint x, mgint y)
{
}

MGP_Cursor* MGP_Cursor_Create(MGSystemCursor cursor)
{
	return new MGP_Cursor { cursor };
}

MGP_Cursor* MGP_Cursor_CreateCustom(mgbyte* rgba, mgint width, mgint height, mgint originx, mgint originy)
{
	return new MGP_Cursor { MGSystemCursor::Arrow };
}

void MGP_Cursor_Destroy(MGP_Cursor* cursor)
{
	delete cursor;
}

mgint MGP_GamePad_GetMaxSupported()
{
	return 0;
}

void MGP_GamePad_GetCaps(MGP_Platform* platform, mgint identifer, MGP_ControllerCaps* caps)
{
	memset(caps, 0, sizeof(MGP_ControllerCaps));
}

mgbyte MGP_GamePad_SetVibration(MGP_Platform* platform, mgint identifer, mgfloat leftMotor, mgfloat rightMotor, mgfloat leftTrigger, mgfloat rightTrigger)
{
	return 0;
}
