namespace Lilia.Api.ErrorPages;

public static class ErrorPageGenerator
{
    private static readonly Dictionary<int, (string Title, string Description, string Emoji, string ExtraHtml)> ErrorMetadata = new()
    {
        [400] = (
            "Bad Request",
            "We couldn't make sense of that one. Double-check and try again?",
            "🤔",
            """<div class="hint">The request was malformed or missing something important.</div>"""
        ),
        [401] = (
            "Who Goes There?",
            "You need to sign in before we can let you through.",
            "🔐",
            """<div class="hint">Try signing in or refreshing your session.</div>"""
        ),
        [403] = (
            "Off Limits",
            "This area is reserved. You don't have the right permissions.",
            "🚫",
            """<div class="hint">If you think this is a mistake, contact the document owner.</div>"""
        ),
        [404] = (
            "Lost in Space",
            "We looked everywhere, but this page is playing hide and seek.",
            "🔭",
            """
            <div class="stars" id="stars"></div>
            <div class="hint">Maybe it was moved, renamed, or it never existed in the first place.</div>
            """
        ),
        [500] = (
            "Oops, We Broke Something",
            "Our hamsters stopped running. We're getting them back on the wheel.",
            "🐹",
            """
            <div class="progress-bar"><div class="progress-fill"></div></div>
            <div class="hint">Our team has been notified. Try again in a moment.</div>
            """
        ),
        [502] = (
            "Bad Gateway",
            "We asked an upstream server for help and got nonsense back.",
            "⛈️",
            """<div class="hint">This usually resolves on its own. Give it a minute.</div>"""
        ),
        [503] = (
            "Taking a Breather",
            "We're doing some maintenance to make things even better.",
            "🛠️",
            """
            <div class="countdown" id="countdown">Retrying in <span id="timer">30</span>s</div>
            <div class="hint">We'll be back before you can finish your coffee.</div>
            """
        ),
    };

    public static string GenerateHtml(int statusCode, string homeUrl = "/", string? reviewUrl = null)
    {
        var (title, description, emoji, extraHtml) = ErrorMetadata.TryGetValue(statusCode, out var meta)
            ? meta
            : ("Something Went Wrong", "An unexpected error occurred. That's all we know.", "😵", """<div class="hint">Try going back or returning home.</div>""");

        var pageSpecificCss = statusCode switch
        {
            404 => """
                .stars { position: fixed; inset: 0; pointer-events: none; overflow: hidden; z-index: 0; }
                .star {
                    position: absolute;
                    width: 3px; height: 3px;
                    background: var(--primary);
                    border-radius: 50%;
                    opacity: 0;
                    animation: twinkle 3s ease-in-out infinite;
                }
                @keyframes twinkle {
                    0%, 100% { opacity: 0; transform: scale(0.5); }
                    50% { opacity: 0.8; transform: scale(1.2); }
                }
                """,
            500 => """
                .progress-bar {
                    width: 200px;
                    height: 6px;
                    background: var(--border);
                    border-radius: 3px;
                    overflow: hidden;
                    margin: 0 auto 16px;
                }
                .progress-fill {
                    height: 100%;
                    width: 30%;
                    background: var(--primary);
                    border-radius: 3px;
                    animation: loading 2s ease-in-out infinite;
                }
                @keyframes loading {
                    0% { width: 0%; margin-left: 0; }
                    50% { width: 60%; margin-left: 20%; }
                    100% { width: 0%; margin-left: 100%; }
                }
                """,
            503 => """
                .countdown {
                    font-size: 14px;
                    color: var(--text-muted);
                    margin-bottom: 12px;
                    font-variant-numeric: tabular-nums;
                }
                """,
            _ => ""
        };

        var pageSpecificJs = statusCode switch
        {
            404 => """
                <script>
                (function() {
                    var c = document.getElementById('stars');
                    if (!c) return;
                    for (var i = 0; i < 40; i++) {
                        var s = document.createElement('div');
                        s.className = 'star';
                        s.style.left = Math.random() * 100 + '%';
                        s.style.top = Math.random() * 100 + '%';
                        s.style.animationDelay = (Math.random() * 3) + 's';
                        s.style.animationDuration = (2 + Math.random() * 3) + 's';
                        c.appendChild(s);
                    }
                })();
                </script>
                """,
            503 => """
                <script>
                (function() {
                    var t = 30, el = document.getElementById('timer');
                    if (!el) return;
                    var iv = setInterval(function() {
                        t--;
                        el.textContent = t;
                        if (t <= 0) { clearInterval(iv); location.reload(); }
                    }, 1000);
                })();
                </script>
                """,
            _ => ""
        };

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{{statusCode}} — {{title}} | Lilia</title>
            <style>
                *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}

                :root {
                    --bg: #fafafa;
                    --surface: #ffffff;
                    --text: #1a1a1a;
                    --text-muted: #6b7280;
                    --primary: #2563eb;
                    --primary-hover: #1d4ed8;
                    --border: #e5e7eb;
                    --shadow: rgba(0,0,0,0.08);
                }

                @media (prefers-color-scheme: dark) {
                    :root {
                        --bg: #0f0f0f;
                        --surface: #1a1a1a;
                        --text: #e5e5e5;
                        --text-muted: #9ca3af;
                        --primary: #3b82f6;
                        --primary-hover: #60a5fa;
                        --border: #2d2d2d;
                        --shadow: rgba(0,0,0,0.3);
                    }
                }

                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
                    background: var(--bg);
                    color: var(--text);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 24px;
                }

                .card {
                    position: relative;
                    z-index: 1;
                    background: var(--surface);
                    border: 1px solid var(--border);
                    border-radius: 20px;
                    box-shadow: 0 8px 32px var(--shadow);
                    padding: 52px 44px 44px;
                    max-width: 480px;
                    width: 100%;
                    text-align: center;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1);
                }

                @keyframes slideUp {
                    from { opacity: 0; transform: translateY(30px) scale(0.97); }
                    to   { opacity: 1; transform: translateY(0) scale(1); }
                }

                .emoji {
                    font-size: 64px;
                    line-height: 1;
                    margin-bottom: 20px;
                    display: inline-block;
                    animation: pop 0.5s cubic-bezier(0.16, 1, 0.3, 1) 0.2s both, wiggle 4s ease-in-out 1s infinite;
                    cursor: default;
                    user-select: none;
                }

                .emoji:hover {
                    animation: spin 0.6s cubic-bezier(0.16, 1, 0.3, 1);
                }

                @keyframes pop {
                    from { opacity: 0; transform: scale(0); }
                    to   { opacity: 1; transform: scale(1); }
                }

                @keyframes wiggle {
                    0%, 100% { transform: rotate(0deg); }
                    25% { transform: rotate(-6deg); }
                    75% { transform: rotate(6deg); }
                }

                @keyframes spin {
                    from { transform: rotate(0deg) scale(1); }
                    50%  { transform: rotate(180deg) scale(1.2); }
                    to   { transform: rotate(360deg) scale(1); }
                }

                .status-code {
                    font-size: 80px;
                    font-weight: 900;
                    letter-spacing: -4px;
                    background: linear-gradient(135deg, var(--primary), #8b5cf6);
                    -webkit-background-clip: text;
                    -webkit-text-fill-color: transparent;
                    background-clip: text;
                    line-height: 1;
                    margin-bottom: 8px;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.1s both;
                }

                .title {
                    font-size: 22px;
                    font-weight: 700;
                    margin-bottom: 10px;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.15s both;
                }

                .description {
                    font-size: 15px;
                    color: var(--text-muted);
                    line-height: 1.65;
                    margin-bottom: 8px;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.2s both;
                }

                .hint {
                    font-size: 13px;
                    color: var(--text-muted);
                    opacity: 0.7;
                    margin-bottom: 28px;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.25s both;
                }

                .actions {
                    display: flex;
                    gap: 12px;
                    justify-content: center;
                    flex-wrap: wrap;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.3s both;
                }

                .btn {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    padding: 11px 28px;
                    border-radius: 12px;
                    font-size: 14px;
                    font-weight: 600;
                    text-decoration: none;
                    cursor: pointer;
                    transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
                    border: none;
                }

                .btn-primary {
                    background: linear-gradient(135deg, var(--primary), #7c3aed);
                    color: #fff;
                    box-shadow: 0 2px 12px rgba(37, 99, 235, 0.3);
                }

                .btn-primary:hover {
                    transform: translateY(-2px);
                    box-shadow: 0 4px 20px rgba(37, 99, 235, 0.4);
                }

                .btn-primary:active {
                    transform: translateY(0);
                }

                .btn-secondary {
                    background: transparent;
                    color: var(--text-muted);
                    border: 1.5px solid var(--border);
                }

                .btn-secondary:hover {
                    color: var(--text);
                    border-color: var(--text-muted);
                    transform: translateY(-2px);
                }

                .btn-secondary:active {
                    transform: translateY(0);
                }

                .brand {
                    margin-top: 28px;
                    font-size: 13px;
                    font-weight: 500;
                    color: var(--text-muted);
                    opacity: 0.5;
                    letter-spacing: 0.5px;
                    animation: slideUp 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.35s both;
                }

                .divider {
                    width: 48px;
                    height: 3px;
                    background: linear-gradient(135deg, var(--primary), #8b5cf6);
                    border-radius: 2px;
                    margin: 0 auto 24px;
                    opacity: 0.5;
                }

                {{pageSpecificCss}}

                @media (max-width: 500px) {
                    .card { padding: 40px 24px 36px; }
                    .status-code { font-size: 60px; letter-spacing: -3px; }
                    .title { font-size: 18px; }
                    .emoji { font-size: 48px; }
                }
            </style>
        </head>
        <body>
            {{extraHtml}}
            <div class="card">
                <div class="emoji">{{emoji}}</div>
                <div class="status-code">{{statusCode}}</div>
                <h1 class="title">{{title}}</h1>
                <div class="divider"></div>
                <p class="description">{{description}}</p>
                <div class="actions">
                    <a href="javascript:history.back()" class="btn btn-secondary">Go Back</a>
                    {{(reviewUrl is not null ? $"""<a href="{reviewUrl}" class="btn btn-secondary">Back to Review</a>""" : "")}}
                    <a href="{{homeUrl}}" class="btn btn-primary">Take Me Home</a>
                </div>
                <div class="brand">Lilia</div>
            </div>
            {{pageSpecificJs}}
        </body>
        </html>
        """;
    }
}
