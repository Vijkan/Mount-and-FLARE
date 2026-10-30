# Local AI Chat — Bannerlord mod

Talk freely with any lord, lady, companion or notable, answered by your own local LLM.
In any conversation pick **"I'd like to speak with you freely."**, type what you want to say,
and the character answers in character. Each character remembers your past talks (saved in
your save file).

The prompt includes the character's name, age, culture, clan, kingdom, location, personality
traits, encyclopedia bio, relation to you, marriage and war status.

## 1. Start the AI server

Any OpenAI-compatible server works. With llama.cpp:

```
llama-server -m Qwen3.6-35B-A3B-uncensored-heretic-Q3_K_M.gguf -c 8192 -ngl 99 --host 127.0.0.1 --port 8080
```

Check it answers at http://127.0.0.1:8080 before launching the game.

**Your hardware:** the game will use the R9700, so put the model on the WX 9100 so they don't
fight over VRAM. Run `llama-server --list-devices`, then add `--device Vulkan1` (or whichever
number the WX 9100 is). The WX 9100 has 16 GB, so the 35B-A3B at Q3_K_M (16.9 GB) won't fully
fit — add `--n-cpu-moe 8` (raise it until it loads) to keep some expert layers in system RAM.
A smaller model (a 12–14B at Q4) fits outright and answers faster.

Running the server on another PC? Start it with `--host 0.0.0.0` and put that PC's LAN IP in
`config.json`.

## 2. Build and install

Needs the .NET SDK (6 or newer) — https://dotnet.microsoft.com/download

```
cd LocalAIChat
dotnet build -c Release
```

If Bannerlord isn't in the default Steam folder:

```
dotnet build -c Release /p:GameFolder="D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
```

The build copies everything into `<Bannerlord>\Modules\LocalAIChat\`. Enable **Local AI Chat**
in the launcher's Mods tab and start a campaign.

## 3. Settings — `Modules\LocalAIChat\config.json`

| Key | What it does |
|---|---|
| `Endpoint` | Chat completions URL of your server |
| `Model` | Model name (llama-server ignores it) |
| `Temperature` | Creativity, 0.6–1.0 is good for roleplay |
| `MaxTokens` | Max reply length |
| `HistoryTurns` | How many past exchanges each character remembers |
| `TimeoutSeconds` | How long to wait for a reply |
| `DisableThinking` | Turns off Qwen3 reasoning so replies are fast |
| `ExtraSystemPrompt` | Anything extra you want every character to follow |

Restart the game after editing.

## Notes

- Game UI updates only happen on the game thread; HTTP replies are queued and shown on the next tick.
- Remove the mod and existing saves still load; the chat history is just ignored.
