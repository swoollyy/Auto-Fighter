# `<link>` Tag Reference

Drop `<link="id">text</link>` into dialogue lines. `id` can optionally include modifiers after a colon.

---

## Typewriter tags

### `slow` — slower reveal
```
<link="slow">text</link>
```

### `fast` — faster reveal
```
<link="fast">text</link>
```

### `pause` — very slow reveal (use on `...` or a word for a beat)
```
<link="pause">...</link>
```

### `speed:N` — custom reveal multiplier (1 = normal, 0.5 = half speed, 2 = double)
```
<link="speed:0.3">text</link>
<link="speed:2">text</link>
```

### `hold:N` — wait N seconds before revealing the span
```
<link="hold:0.5">text</link>
<link="hold:1.2">text</link>
```

---

## Visual effect tags

Every effect tag accepts modifiers after a colon. Multipliers are relative to component defaults (`:1` = unchanged).

Forms:
- `<link="tag">` — defaults
- `<link="tag:N">` — positional shorthand (maps to the effect's primary modifier)
- `<link="tag:key=N,key=N">` — explicit keys (`,` or `;` separator)

### `jitter` — shake / vibrate
Modifiers:
- `amp` — shake distance (positional shorthand)
- `spd` — shake speed

```
<link="jitter">text</link>
<link="jitter:3">text</link>
<link="jitter:amp=0.5">text</link>
<link="jitter:amp=2,spd=0.5">text</link>
```

### `wave` — vertical sine wave
Modifiers:
- `amp` — wave height (positional shorthand)

```
<link="wave">text</link>
<link="wave:2.5">text</link>
<link="wave:amp=0.4">text</link>
```

### `pop` — pulsing scale
Modifiers:
- `amp` — pulse size (positional shorthand)

```
<link="pop">text</link>
<link="pop:2">text</link>
<link="pop:amp=0.4">text</link>
```

### `rainbow` — cycling hue
Modifiers:
- `hue` — gradient steepness / color variety across chars (positional shorthand)
- `phase` (or `offset`) — shift starting hue (0..1 wraps around)
- `rev` — reverse hue direction for this span (`1` = reverse)
- `dir` — direction override (`1` forward, `-1` reverse)

```
<link="rainbow">text</link>
<link="rainbow:3">text</link>
<link="rainbow:hue=0.2">text</link>
<link="rainbow:hue=2,phase=0.35">text</link>
<link="rainbow:hue=2,rev=1">text</link>
<link="rainbow:hue=2,dir=-1,phase=0.1">text</link>
```

---

## Combining effects

Nest tags to stack multiple effects on the same span:
```
<link="wave:2"><link="rainbow:3">EPIC</link></link>
<link="slow"><link="jitter:2">I see you.</link></link>
```

---

## Placeholder tokens (not link tags)

| Token | Value |
|---|---|
| `{player_name}` | Saved player name |
| `{class}` | Saved character class |

---

## Default TMP rich-text tags (not custom link tags)

These come from TextMeshPro itself and can be mixed with your custom `<link="...">` tags.

Common examples:

### Basic style
```
<b>bold</b>
<i>italic</i>
<u>underline</u>
<s>strikethrough</s>
```

### Color and opacity
```
<color=#FFAA00>orange</color>
<color=red>named color</color>
<alpha=#88>semi-transparent</alpha>
```

### Size and spacing
```
<size=28>big text</size>
<cspace=0.2em>extra character spacing</cspace>
<line-height=120%>taller lines</line-height>
```

### Position and transform-like tags
```
<voffset=0.2em>raised text</voffset>
<sub>subscript</sub>
<sup>superscript</sup>
<rotate=10>rotated glyphs</rotate>
```

### Other useful TMP tags
```
<br>             // line break
<noparse><b>literal tags</b></noparse>
<font="SomeTMPFontAsset">font switch</font>
<sprite=0>       // inline sprite from TMP sprite asset
```
