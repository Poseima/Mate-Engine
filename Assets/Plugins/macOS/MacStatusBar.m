//
// MacStatusBar.m
// Native macOS plugin for Unity — NSStatusBar menu bar icon + NSMenu + Dock control
//
// Compiled into MacStatusBar.bundle via:
//   clang -fobjc-arc -framework Cocoa -framework AppKit -bundle -o MacStatusBar.bundle MacStatusBar.m
//

#import <Cocoa/Cocoa.h>
#import <AppKit/AppKit.h>

// --- Callback types ---
typedef void (*MenuClickCallback)(int itemId);
typedef void (*StatusBarClickCallback)(int clickType); // 0=left, 1=right

// --- Static state ---
static NSStatusItem *sStatusItem = nil;
static NSMenu *sMenu = nil;
static MenuClickCallback sMenuCallback = NULL;
static StatusBarClickCallback sClickCallback = NULL;
static NSImage *sIcon = nil;

// --- Helper: Obj-C target for menu item actions ---
@interface MacStatusBarHelper : NSObject
+ (instancetype)shared;
- (void)menuItemClicked:(NSMenuItem *)sender;
- (void)statusBarClicked:(id)sender;
@end

@implementation MacStatusBarHelper

+ (instancetype)shared {
    static MacStatusBarHelper *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[MacStatusBarHelper alloc] init];
    });
    return instance;
}

- (void)menuItemClicked:(NSMenuItem *)sender {
    if (sMenuCallback) {
        sMenuCallback((int)sender.tag);
    }
}

- (void)statusBarClicked:(id)sender {
    if (sClickCallback) {
        sClickCallback(0); // left click
    }
}

@end

// --- Exported C functions for Unity DllImport ---

void MacStatusBar_Init(const char *tooltip) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sStatusItem != nil) return;

        sStatusItem = [[NSStatusBar systemStatusBar] statusItemWithLength:NSSquareStatusItemLength];
        if (tooltip) {
            sStatusItem.button.toolTip = [NSString stringWithUTF8String:tooltip];
        }

        // Default: show app icon
        sStatusItem.button.image = [NSImage imageNamed:NSImageNameApplicationIcon];
        [sStatusItem.button.image setSize:NSMakeSize(18, 18)];

        sStatusItem.button.target = [MacStatusBarHelper shared];
        sStatusItem.button.action = @selector(statusBarClicked:);
        [sStatusItem.button sendActionOn:NSEventMaskLeftMouseUp | NSEventMaskRightMouseUp];
    });
}

void MacStatusBar_SetIcon(const unsigned char *rgbaPixels, int width, int height) {
    if (!rgbaPixels || width <= 0 || height <= 0) return;

    // Copy pixel data (RGBA -> NSBitmapImageRep)
    size_t dataLen = (size_t)width * height * 4;
    unsigned char *copy = (unsigned char *)malloc(dataLen);
    memcpy(copy, rgbaPixels, dataLen);

    dispatch_async(dispatch_get_main_queue(), ^{
        NSBitmapImageRep *rep = [[NSBitmapImageRep alloc]
            initWithBitmapDataPlanes:&copy
                          pixelsWide:width
                          pixelsHigh:height
                       bitsPerSample:8
                     samplesPerPixel:4
                            hasAlpha:YES
                            isPlanar:NO
                      colorSpaceName:NSDeviceRGBColorSpace
                        bitmapFormat:NSBitmapFormatAlphaNonpremultiplied
                         bytesPerRow:width * 4
                        bitsPerPixel:32];

        if (rep) {
            NSImage *img = [[NSImage alloc] initWithSize:NSMakeSize(18, 18)];
            [img addRepresentation:rep];
            [img setTemplate:NO];

            if (sStatusItem) {
                sStatusItem.button.image = img;
            }

            sIcon = img;
        }

        free(copy);
    });
}

void MacStatusBar_SetTooltip(const char *tooltip) {
    if (!tooltip) return;
    NSString *tip = [NSString stringWithUTF8String:tooltip];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sStatusItem) {
            sStatusItem.button.toolTip = tip;
        }
    });
}

void MacStatusBar_RegisterMenuCallback(MenuClickCallback callback) {
    sMenuCallback = callback;
}

void MacStatusBar_RegisterClickCallback(StatusBarClickCallback callback) {
    sClickCallback = callback;
}

void MacStatusBar_ClearMenu(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sMenu) {
            [sMenu removeAllItems];
        } else {
            sMenu = [[NSMenu alloc] init];
        }
    });
}

void MacStatusBar_AddMenuItem(const char *label, int itemId) {
    if (!label) return;
    NSString *title = [NSString stringWithUTF8String:label];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!sMenu) sMenu = [[NSMenu alloc] init];

        NSMenuItem *item = [[NSMenuItem alloc]
            initWithTitle:title
                   action:@selector(menuItemClicked:)
            keyEquivalent:@""];
        item.target = [MacStatusBarHelper shared];
        item.tag = itemId;
        [sMenu addItem:item];
    });
}

void MacStatusBar_AddSeparator(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!sMenu) sMenu = [[NSMenu alloc] init];
        [sMenu addItem:[NSMenuItem separatorItem]];
    });
}

void MacStatusBar_ShowMenu(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sStatusItem && sMenu) {
            sStatusItem.menu = sMenu;
            [sStatusItem.button performClick:nil];
            // Reset menu after display so click handler works again
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)),
                dispatch_get_main_queue(), ^{
                    sStatusItem.menu = nil;
                });
        }
    });
}

void MacStatusBar_Destroy(void) {
    // Null callbacks first to prevent calling back into torn-down managed code
    sMenuCallback = NULL;
    sClickCallback = NULL;

    // Must run synchronously — if we dispatch_async, Unity may tear down
    // before the block executes, leaving dangling pointers that crash in objc_retain.
    if ([NSThread isMainThread]) {
        if (sStatusItem) {
            [[NSStatusBar systemStatusBar] removeStatusItem:sStatusItem];
            sStatusItem = nil;
        }
        sMenu = nil;
        sIcon = nil;
    } else {
        dispatch_sync(dispatch_get_main_queue(), ^{
            if (sStatusItem) {
                [[NSStatusBar systemStatusBar] removeStatusItem:sStatusItem];
                sStatusItem = nil;
            }
            sMenu = nil;
            sIcon = nil;
        });
    }
}

// --- Dock visibility ---

void MacStatusBar_SetDockVisible(int visible) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (visible) {
            [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
        } else {
            [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        }
    });
}

int MacStatusBar_IsDockVisible(void) {
    return ([NSApp activationPolicy] == NSApplicationActivationPolicyRegular) ? 1 : 0;
}

// --- Notifications ---

void MacStatusBar_ShowNotification(const char *title, const char *message) {
    if (!title || !message) return;
    NSString *nsTitle = [NSString stringWithUTF8String:title];
    NSString *nsMessage = [NSString stringWithUTF8String:message];

    dispatch_async(dispatch_get_main_queue(), ^{
        NSUserNotification *notification = [[NSUserNotification alloc] init];
        notification.title = nsTitle;
        notification.informativeText = nsMessage;
        notification.soundName = NSUserNotificationDefaultSoundName;
        [[NSUserNotificationCenter defaultUserNotificationCenter] deliverNotification:notification];
    });
}
