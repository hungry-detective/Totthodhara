// Tiny vector meter icons (CPU / RAM / net arrows). Drawn, not font
// glyphs, so they render identically on every Windows machine.
// Style: thin 1.2px strokes, minimal monitor-widget look.
import QtQuick

Canvas {
    property string kind: "cpu"   // cpu | ram | down | up | code | clock
    property color ink: AppState.text

    implicitWidth: (kind === "ram" || kind === "code") ? 24 : 18
    implicitHeight: 18
    onInkChanged: requestPaint()
    onKindChanged: requestPaint()
    Component.onCompleted: requestPaint()

    onPaint: {
        const ctx = getContext("2d")
        ctx.reset()
        ctx.strokeStyle = ink
        ctx.fillStyle = ink
        ctx.lineWidth = 1.6
        ctx.lineCap = "round"
        ctx.lineJoin = "round"
        // Rounded-rect path (Canvas2D has no roundRect here).
        const rr = (x, y, w, h, r) => {
            ctx.beginPath()
            ctx.moveTo(x + r, y)
            ctx.lineTo(x + w - r, y)
            ctx.arcTo(x + w, y, x + w, y + r, r)
            ctx.lineTo(x + w, y + h - r)
            ctx.arcTo(x + w, y + h, x + w - r, y + h, r)
            ctx.lineTo(x + r, y + h)
            ctx.arcTo(x, y + h, x, y + h - r, r)
            ctx.lineTo(x, y + r)
            ctx.arcTo(x, y, x + r, y, r)
            ctx.closePath()
        }
        if (kind === "cpu") {
            // Rounded square with a pulse / line-graph zig-zag inside.
            // Slightly larger box than before so it balances the RAM module.
            rr(2.5, 2.5, 13, 13, 3)
            ctx.stroke()
            ctx.beginPath()
            ctx.moveTo(4.8, 9.5)
            ctx.lineTo(7, 9.5)
            ctx.lineTo(8.2, 6)
            ctx.lineTo(10.2, 12)
            ctx.lineTo(11.6, 8.2)
            ctx.lineTo(13.4, 8.2)
            ctx.stroke()
        } else if (kind === "ram") {
            // Wide module (clearly rectangular, never square): thin
            // rounded outline, contact pins below. Same 12px height
            // as the CPU box (y 3-15) so both icons match. Outline runs
            // thinner (1px) than the other meter glyphs. No inner text:
            // the shelf label already says RAM, and 6px type inside the
            // box only muddies it — contact pins below carry the meaning.
            // Slightly slimmer than before so it balances the CPU box.
            ctx.lineWidth = 1
            rr(4, 3, 16, 12, 1.5)
            ctx.stroke()
            // Contact teeth: short ticks under the module edge.
            ctx.beginPath()
            for (let px = 8.2; px <= 16.1; px += 3.9) {
                ctx.moveTo(px, 15)
                ctx.lineTo(px, 17)
            }
            ctx.stroke()
        } else if (kind === "code") {
            // Code brackets: </> drawn minimal.
            ctx.beginPath(); ctx.moveTo(10.5, 4.5); ctx.lineTo(6, 9); ctx.lineTo(10.5, 13.5); ctx.stroke()
            ctx.beginPath(); ctx.moveTo(12.5, 15); ctx.lineTo(15.5, 3); ctx.stroke()
            ctx.beginPath(); ctx.moveTo(17.5, 4.5); ctx.lineTo(22, 9); ctx.lineTo(17.5, 13.5); ctx.stroke()
        } else if (kind === "clock") {
            // Clock face: thin ring + hour/minute hands, same 13px box
            // and 1.6px pen as the CPU icon so the two balance.
            ctx.beginPath(); ctx.arc(9, 9, 6.5, 0, Math.PI * 2); ctx.stroke()
            ctx.beginPath(); ctx.moveTo(9, 9); ctx.lineTo(9, 4.3); ctx.stroke()
            ctx.beginPath(); ctx.moveTo(9, 9); ctx.lineTo(12.7, 10.7); ctx.stroke()
        } else if (kind === "down" || kind === "up") {
            // Thin stem, wide OPEN head (two long strokes, unfilled V).
            ctx.lineWidth = 1.5
            const dir = kind === "up" ? -1 : 1
            const tip = 9 + dir * 6
            const base = 9 - dir * 6
            ctx.beginPath(); ctx.moveTo(9, tip); ctx.lineTo(9, base); ctx.stroke()
            ctx.beginPath()
            ctx.moveTo(9 - 4.5, tip - dir * 4.5)
            ctx.lineTo(9, tip)
            ctx.lineTo(9 + 4.5, tip - dir * 4.5)
            ctx.stroke()
        }
    }
}
