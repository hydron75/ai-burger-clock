// Generates Mac/Assets.xcassets/AppIcon.appiconset from code so the icon stays reproducible.
// Run from the repository root: swift Mac/tools/make-app-icon.swift
// Green matches TrayAppearance's FULL THROTTLE color; the glyph matches the menu-bar "F".
import AppKit

let output = URL(fileURLWithPath: "Mac/Assets.xcassets/AppIcon.appiconset", isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func color(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: r / 255, green: g / 255, blue: b / 255, alpha: a)
}

func render(pixels: Int) -> Data {
    let size = CGFloat(pixels)
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let context = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
                            space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    // Full-bleed square: macOS applies its own icon shape mask.
    let gradient = CGGradient(colorsSpace: space,
                              colors: [color(38, 196, 110), color(18, 128, 70)] as CFArray,
                              locations: [0, 1])!
    context.drawLinearGradient(gradient, start: CGPoint(x: 0, y: size), end: CGPoint(x: 0, y: 0), options: [])

    // Clock face: white ring with twelve hour ticks.
    let center = CGPoint(x: size / 2, y: size / 2)
    let radius = size * 0.34
    context.setStrokeColor(color(255, 255, 255))
    context.setLineWidth(size * 0.035)
    context.strokeEllipse(in: CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2))
    context.setLineCap(.round)
    for hour in 0..<12 {
        let angle = CGFloat(hour) * .pi / 6
        let inner = radius * (hour % 3 == 0 ? 0.80 : 0.86)
        let outer = radius * 0.93
        context.setLineWidth(size * (hour % 3 == 0 ? 0.022 : 0.013))
        context.move(to: CGPoint(x: center.x + inner * sin(angle), y: center.y + inner * cos(angle)))
        context.addLine(to: CGPoint(x: center.x + outer * sin(angle), y: center.y + outer * cos(angle)))
        context.strokePath()
    }

    // Bold "F" centered on the glyph's own bounds.
    let font = NSFont.systemFont(ofSize: size * 0.40, weight: .heavy)
    let text = NSAttributedString(string: "F", attributes: [.font: font, .foregroundColor: NSColor.white])
    let line = CTLineCreateWithAttributedString(text)
    let bounds = CTLineGetBoundsWithOptions(line, .useGlyphPathBounds)
    context.textPosition = CGPoint(x: center.x - bounds.midX, y: center.y - bounds.midY)
    CTLineDraw(line, context)

    let rep = NSBitmapImageRep(cgImage: context.makeImage()!)
    return rep.representation(using: .png, properties: [:])!
}

// macOS app icon slots: 16, 32, 128, 256, 512 points at 1x and 2x.
var images: [String] = []
for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = "icon_\(points)x\(points)\(scale == 2 ? "@2x" : "").png"
        try render(pixels: points * scale).write(to: output.appendingPathComponent(name))
        images.append("""
            { "filename" : "\(name)", "idiom" : "mac", "scale" : "\(scale)x", "size" : "\(points)x\(points)" }
        """)
    }
}
let contents = """
{
  "images" : [
\(images.joined(separator: ",\n"))
  ],
  "info" : { "author" : "xcode", "version" : 1 }
}

"""
try contents.write(to: output.appendingPathComponent("Contents.json"), atomically: true, encoding: .utf8)
try """
{
  "info" : { "author" : "xcode", "version" : 1 }
}

""".write(to: output.deletingLastPathComponent().appendingPathComponent("Contents.json"), atomically: true, encoding: .utf8)
print("Wrote \(images.count) icon images to \(output.path)")
