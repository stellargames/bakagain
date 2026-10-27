package org.bakagain.setup;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.provider.OpenableColumns;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * The setup wizard's Android side (TASK-1014): the system pickers, opened from a headless fragment, and
 * the copy of what was picked into a staging folder, on a worker thread. The C# side imports from there.
 */
public class BakSetupPicker extends Fragment {
    public interface Callback {
        void onPicked(String json);
        void onCancelled();
    }

    private static final String[] REQUIRED = { "KRONDOR.001", "FRP.SX", "STARTUP.GAM" };
    private static final String CONFIG = "RESOURCE.CFG";

    // Static, not fields: a fragment the system recreates (rotation, background) still delivers its result.
    private static Callback pending;
    private static boolean pickingFolder;
    private static String destination;

    public static void pickFolder(Activity activity, String destDir, Callback callback) { start(activity, true, destDir, callback); }

    public static void pickFiles(Activity activity, String destDir, Callback callback) { start(activity, false, destDir, callback); }

    private static void start(Activity activity, boolean folder, String destDir, Callback callback) {
        if (pending != null) pending.onCancelled();
        pending = callback;
        pickingFolder = folder;
        destination = destDir;
        activity.runOnUiThread(() ->
            activity.getFragmentManager().beginTransaction().add(new BakSetupPicker(), "BakSetupPicker").commitAllowingStateLoss());
    }

    @Override
    public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if (saved != null) return; // recreated while the picker is open: its result still comes to onActivityResult
        Intent intent;
        if (pickingFolder) {
            intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE); // no EXTRA_LOCAL_ONLY: cloud drives are listed too
        } else {
            intent = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*")
                .putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        }
        // Opens in Download, where the help tells players to put the files (one tap fewer).
        intent.putExtra(DocumentsContract.EXTRA_INITIAL_URI,
            DocumentsContract.buildDocumentUri("com.android.externalstorage.documents", "primary:Download"));
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try {
            startActivityForResult(intent, 1);
        } catch (ActivityNotFoundException e) {
            Callback callback = pending;
            pending = null;
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            if (callback != null) callback.onPicked(result(null, new JSONArray(), 0, "no-picker"));
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        Callback callback = pending;
        pending = null;
        if (getFragmentManager() != null) getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        if (callback == null) return;
        if (resultCode != Activity.RESULT_OK || data == null) {
            callback.onCancelled();
            return;
        }
        if (getActivity() == null) { // detached before the result came: nothing to copy with
            callback.onPicked(result(null, new JSONArray(), 0, "io"));
            return;
        }
        Context context = getActivity().getApplicationContext();
        boolean folder = pickingFolder;
        String dest = destination;
        List<String> uris = new ArrayList<>();
        ClipData clip = data.getClipData();
        if (clip != null) {
            for (int i = 0; i < clip.getItemCount(); i++) uris.add(clip.getItemAt(i).getUri().toString());
        } else if (data.getData() != null) {
            uris.add(data.getData().toString());
        }
        if (uris.isEmpty()) {
            callback.onCancelled();
            return;
        }
        new Thread(() -> {
            String json;
            try {
                json = folder ? copyTree(context, uris.get(0), dest) : copyUris(context, uris.toArray(new String[0]), dest);
            } catch (Throwable t) { // exactly one callback, whatever happens: C# waits for it
                json = result(null, new JSONArray(), 0, "io");
            }
            callback.onPicked(json);
        }).start();
    }

    private static final class Doc {
        final String id;
        final long size;
        final boolean dir;

        Doc(String id, long size, boolean dir) { this.id = id; this.size = size; this.dir = dir; }
    }

    /** The folder holding the three files, at most two levels down, the shallowest first; else the one holding the most. */
    public static String copyTree(Context context, String treeUri, String destDir) {
        ContentResolver resolver = context.getContentResolver();
        Uri tree = Uri.parse(treeUri);
        String name = null;
        Map<String, Doc> best = new HashMap<>();
        int bestFound = -1;
        String error = null;
        JSONArray copied = new JSONArray();
        try {
            resolver.takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (SecurityException e) {
            // Not offered by every provider; the one-time grant of the pick is enough to copy.
        }
        try {
            String rootId = DocumentsContract.getTreeDocumentId(tree);
            name = displayName(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, rootId));
            List<String> level = new ArrayList<>();
            level.add(rootId);
            for (int depth = 0; depth <= 2 && bestFound < REQUIRED.length && !level.isEmpty(); depth++) {
                List<String> next = new ArrayList<>();
                for (String id : level) {
                    Map<String, Doc> kids = children(resolver, tree, id);
                    int found = 0;
                    for (String r : REQUIRED) if (kids.containsKey(r) && !kids.get(r).dir) found++;
                    if (found > bestFound) {
                        bestFound = found;
                        best = kids;
                    }
                    for (Doc d : kids.values()) if (d.dir) next.add(d.id);
                }
                level = next;
            }
            Map<String, Doc> files = new HashMap<>(best);
            if (!files.containsKey(CONFIG) && best.containsKey("__SUPPORT")) {
                Doc save = children(resolver, tree, best.get("__SUPPORT").id).get("SAVE");
                if (save != null && save.dir) {
                    Doc cfg = children(resolver, tree, save.id).get(CONFIG);
                    if (cfg != null) files.put(CONFIG, cfg);
                }
            }
            for (String target : new String[] { REQUIRED[0], REQUIRED[1], REQUIRED[2], CONFIG }) {
                Doc d = files.get(target);
                if (d == null || d.dir) continue;
                error = copy(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, d.id), d.size, new File(destDir, target));
                if (error != null) break;
                copied.put(target);
            }
        } catch (SecurityException e) {
            error = "permission";
        } catch (RuntimeException e) {
            error = "io";
        } finally {
            try {
                resolver.releasePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            } catch (SecurityException e) {
                // Never taken.
            }
        }
        return result(name, copied, Math.max(bestFound, 0), error);
    }

    /** Picked files: the game's own by name, upper-cased; else one file (a zip or an installer) as it is. */
    public static String copyUris(Context context, String[] uris, String destDir) {
        ContentResolver resolver = context.getContentResolver();
        String name = null;
        String error = null;
        JSONArray copied = new JSONArray();
        int found = 0;
        try {
            List<String> names = new ArrayList<>();
            List<Long> sizes = new ArrayList<>();
            for (String u : uris) {
                String[] meta = meta(resolver, Uri.parse(u));
                names.add(meta[0] == null ? "" : meta[0]);
                sizes.add(meta[1] == null ? -1L : Long.parseLong(meta[1]));
            }
            name = names.get(0);
            for (int i = 0; i < uris.length && error == null; i++) {
                String upper = names.get(i).toUpperCase(Locale.ROOT);
                boolean required = false;
                for (String r : REQUIRED) required |= r.equals(upper);
                if (!required && !CONFIG.equals(upper)) continue;
                if (required) found++;
                error = copy(resolver, Uri.parse(uris[i]), sizes.get(i), new File(destDir, upper));
                if (error == null) copied.put(upper);
            }
            if (copied.length() == 0 && error == null && uris.length == 1 && !name.isEmpty()) {
                String safe = new File(name).getName();
                error = copy(resolver, Uri.parse(uris[0]), sizes.get(0), new File(destDir, safe));
                if (error == null) copied.put(safe);
            }
        } catch (SecurityException e) {
            error = "permission";
        } catch (RuntimeException e) {
            error = "io";
        }
        return result(name, copied, found, error);
    }

    public static void shareText(Activity activity, String subject, String text) {
        Intent send = new Intent(Intent.ACTION_SEND).setType("text/plain")
            .putExtra(Intent.EXTRA_SUBJECT, subject).putExtra(Intent.EXTRA_TEXT, text);
        activity.startActivity(Intent.createChooser(send, subject));
    }

    /** This app's own logcat (a player on Android writes no log file), the last <code>maxChars</code> of it. */
    public static String readLog(int maxChars) {
        try {
            java.lang.Process p = Runtime.getRuntime().exec(new String[] { "logcat", "-d", "-v", "time", "--pid=" + android.os.Process.myPid() });
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            try (InputStream in = p.getInputStream()) {
                byte[] buffer = new byte[65536];
                int n;
                while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
            }
            String log = out.toString("UTF-8");
            return log.length() > maxChars ? log.substring(log.length() - maxChars) : log;
        } catch (IOException e) {
            return "";
        }
    }

    private static Map<String, Doc> children(ContentResolver resolver, Uri tree, String parentId) {
        Map<String, Doc> map = new HashMap<>();
        String[] columns = { DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_SIZE, DocumentsContract.Document.COLUMN_MIME_TYPE };
        try (Cursor c = resolver.query(DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId), columns, null, null, null)) {
            while (c != null && c.moveToNext()) {
                if (c.getString(1) == null) continue;
                boolean dir = DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(3));
                map.putIfAbsent(c.getString(1).toUpperCase(Locale.ROOT), new Doc(c.getString(0), c.isNull(2) ? -1 : c.getLong(2), dir));
            }
        }
        return map;
    }

    private static String displayName(ContentResolver resolver, Uri uri) {
        return meta(resolver, uri)[0];
    }

    /** { display name, size or null }. */
    private static String[] meta(ContentResolver resolver, Uri uri) {
        try (Cursor c = resolver.query(uri, new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE }, null, null, null)) {
            if (c != null && c.moveToFirst()) return new String[] { c.getString(0), c.isNull(1) ? null : c.getString(1) };
        }
        return new String[] { null, null };
    }

    /** Null, "space" when the disk is full, or "io"; a copy whose length differs from the source's size is an "io". A failed copy leaves no file. */
    private static String copy(ContentResolver resolver, Uri from, long size, File to) {
        String error = copyStream(resolver, from, size, to);
        if (error != null) to.delete();
        return error;
    }

    private static String copyStream(ContentResolver resolver, Uri from, long size, File to) {
        long total = 0;
        try (InputStream in = resolver.openInputStream(from); OutputStream out = new FileOutputStream(to)) {
            if (in == null) return "io";
            byte[] buffer = new byte[65536];
            int n;
            while ((n = in.read(buffer)) > 0) {
                out.write(buffer, 0, n);
                total += n;
            }
        } catch (IOException e) {
            String m = String.valueOf(e.getMessage());
            return m.contains("ENOSPC") || m.contains("No space") ? "space" : "io";
        }
        return size >= 0 && total != size ? "io" : null;
    }

    private static String result(String name, JSONArray copied, int found, String error) {
        try {
            return new JSONObject().put("name", name == null ? JSONObject.NULL : name).put("copied", copied)
                .put("found", found).put("error", error == null ? JSONObject.NULL : error).toString();
        } catch (org.json.JSONException e) {
            return "{\"error\":\"io\"}";
        }
    }
}
