using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using BMEngine;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace MidiTrailRender
{
    public class Render : IPluginRender
    {
        public string Name => "MidiTrail+";
        public string Description => "Clone of the popular tool MidiTrail for black midi rendering. Added exclusive bonus features, and less buggy.";

        public bool Initialized { get; set; } = false;

        public ImageSource PreviewImage => null;

        #region Shaders
        string whiteKeyShaderVert = @"#version 330 core

layout(location=0) in vec3 in_position;
layout(location=1) in float in_brightness;
layout(location=2) in float blend_fac;

out vec4 v2f_color;

uniform mat4 MVP;
uniform vec4 coll;
uniform vec4 colr;

void main()
{
    gl_Position = MVP * vec4(in_position, 1.0);
    v2f_color = vec4((coll.xyz * blend_fac + colr.xyz * (1 - blend_fac)) * in_brightness, 1);
}
";
        string whiteKeyShaderFrag = @"#version 330 core

in vec4 v2f_color;
layout (location=0) out vec4 out_color;

void main()
{
    out_color = v2f_color;
}
";

        string blackKeyShaderVert = @"#version 330 core

layout(location=0) in vec3 in_position;
layout(location=1) in float in_brightness;
layout(location=2) in float blend_fac;

out vec4 v2f_color;

uniform mat4 MVP;
uniform vec4 coll;
uniform vec4 colr;

void main()
{
    gl_Position = MVP * vec4(in_position, 1.0);
    v2f_color = vec4(1 - in_brightness + (coll.xyz * blend_fac + colr.xyz * (1 - blend_fac)) * in_brightness, 1);
}
";
        string blackKeyShaderFrag = @"#version 330 core

in vec4 v2f_color;
layout (location=0) out vec4 out_color;

void main()
{
    out_color = v2f_color;
}
";
        string noteShaderVert = @"#version 330 core

layout(location=0) in vec3 in_position;
layout(location=1) in vec4 in_color;
layout(location=2) in float in_shade;

out vec4 v2f_color;

uniform mat4 MVP;

void main()
{
    gl_Position = MVP * vec4(in_position, 1.0);
    v2f_color = vec4(in_color.xyz + in_shade, in_color.w);
}
";
        string noteShaderFrag = @"#version 330 core

in vec4 v2f_color;
layout (location=0) out vec4 out_color;

void main()
{
    out_color = v2f_color;
}
";

        int MakeShader(string vert, string frag)
        {
            int _vertexObj = GL.CreateShader(ShaderType.VertexShader);
            int _fragObj = GL.CreateShader(ShaderType.FragmentShader);
            int statusCode;
            string info;

            GL.ShaderSource(_vertexObj, vert);
            GL.CompileShader(_vertexObj);
            info = GL.GetShaderInfoLog(_vertexObj);
            GL.GetShader(_vertexObj, ShaderParameter.CompileStatus, out statusCode);
            if (statusCode != 1) throw new ApplicationException(info);

            GL.ShaderSource(_fragObj, frag);
            GL.CompileShader(_fragObj);
            info = GL.GetShaderInfoLog(_fragObj);
            GL.GetShader(_fragObj, ShaderParameter.CompileStatus, out statusCode);
            if (statusCode != 1) throw new ApplicationException(info);

            int shader = GL.CreateProgram();
            GL.AttachShader(shader, _fragObj);
            GL.AttachShader(shader, _vertexObj);
            GL.LinkProgram(shader);
            return shader;
        }
        #endregion

        int whiteKeyShader;
        int blackKeyShader;
        int noteShader;

        int uWhiteKeyMVP;
        int uWhiteKeycoll;
        int uWhiteKeycolr;

        int uBlackKeyMVP;
        int uBlackKeycoll;
        int uBlackKeycolr;

        int uNoteMVP;

        public bool ManualNoteDelete => false;

        public double LastMidiTimePerTick { get; set; } = 500000 / 96.0;

        public double NoteScreenTime => settings.viewdist * settings.deltaTimeOnScreen;

        public long LastNoteCount { get; private set; }

        public Control SettingsControl { get; private set; }

        public int NoteCollectorOffset => -(int)(settings.deltaTimeOnScreen * 0.2);

        bool[] blackKeys = new bool[257];
        int[] keynum = new int[257];

        RenderSettings renderSettings;
        Settings settings;

        int buffer3dtex;
        int buffer3dbuf;

        int whiteKeyVert;
        int whiteKeyCol;
        int whiteKeyIndx;
        int whiteKeyBlend;
        int blackKeyVert;
        int blackKeyCol;
        int blackKeyIndx;
        int blackKeyBlend;

        int noteVert;
        int noteCol;
        int noteIndx;
        int noteShade;

        int noteBuffLen = 2048;

        double[] noteVertBuff;
        float[] noteColBuff;
        float[] noteShadeBuff;
        int[] noteIndxBuff;

        int noteBuffPos = 0;

        public void Dispose()
        {
            GL.DeleteBuffers(12, new int[] {
                whiteKeyVert, whiteKeyCol, blackKeyVert, blackKeyCol,
                whiteKeyIndx, blackKeyIndx, whiteKeyBlend, blackKeyBlend,
                noteVert, noteCol, noteIndx, noteShade
            });
            util.Dispose();
            Initialized = false;
            Console.WriteLine("Disposed of MidiTrailRender");
        }

        Util util;
        public Render(RenderSettings settings)
        {
            this.settings = new Settings();
            this.renderSettings = settings;
            SettingsControl = new SettingsCtrl(this.settings);
            //PreviewImage = BitmapToImageSource(Properties.Resources.preview);
            for (int i = 0; i < blackKeys.Length; i++) blackKeys[i] = isBlackNote(i);
            int b = 0;
            int w = 0;
            for (int i = 0; i < keynum.Length; i++)
            {
                if (blackKeys[i]) keynum[i] = b++;
                else keynum[i] = w++;
            }
        }

        int whiteKeyBufferLen = 0;
        int blackKeyBufferLen = 0;
        public void Init()
        {
            whiteKeyShader = MakeShader(whiteKeyShaderVert, whiteKeyShaderFrag);
            blackKeyShader = MakeShader(blackKeyShaderVert, blackKeyShaderFrag);
            noteShader = MakeShader(noteShaderVert, noteShaderFrag);

            uWhiteKeyMVP = GL.GetUniformLocation(whiteKeyShader, "MVP");
            uWhiteKeycoll = GL.GetUniformLocation(whiteKeyShader, "coll");
            uWhiteKeycolr = GL.GetUniformLocation(whiteKeyShader, "colr");

            uBlackKeyMVP = GL.GetUniformLocation(blackKeyShader, "MVP");
            uBlackKeycoll = GL.GetUniformLocation(blackKeyShader, "coll");
            uBlackKeycolr = GL.GetUniformLocation(blackKeyShader, "colr");

            uNoteMVP = GL.GetUniformLocation(noteShader, "MVP");

            GLUtils.GenFrameBufferTexture(renderSettings.width, renderSettings.height, out buffer3dbuf, out buffer3dtex, true);

            util = new Util();
            Initialized = true;
            Console.WriteLine("Initialised MidiTrailRender");


            whiteKeyVert = GL.GenBuffer();
            whiteKeyCol = GL.GenBuffer();
            whiteKeyIndx = GL.GenBuffer();
            whiteKeyBlend = GL.GenBuffer();

            blackKeyVert = GL.GenBuffer();
            blackKeyCol = GL.GenBuffer();
            blackKeyIndx = GL.GenBuffer();
            blackKeyBlend = GL.GenBuffer();

            noteVert = GL.GenBuffer();
            noteCol = GL.GenBuffer();
            noteIndx = GL.GenBuffer();
            noteShade = GL.GenBuffer();

            noteVertBuff = new double[noteBuffLen * 4 * 3];
            noteColBuff = new float[noteBuffLen * 4 * 4];
            noteShadeBuff = new float[noteBuffLen * 4];

            noteIndxBuff = new int[noteBuffLen * 4];

            for (int i = 0; i < noteIndxBuff.Length; i++) noteIndxBuff[i] = i;
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, noteIndx);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                (IntPtr)(noteIndxBuff.Length * 4),
                noteIndxBuff,
                BufferUsageHint.StaticDraw);

            #region White Key Model
            float whitekeylen = 5.5f;
            double[] verts = new double[] {
                //front
                0, 0, -whitekeylen,
                0, 0.7, -whitekeylen,
                1, 0.7, -whitekeylen,
                1, 0, -whitekeylen,
                //front dark
                0, 0.7, -whitekeylen,
                0, 1, -whitekeylen,
                1, 1, -whitekeylen,
                1, 0.7, -whitekeylen,
                //top
                1, 1, 0,
                1, 1, -whitekeylen,
                0, 1, -whitekeylen,
                0, 1, 0,
                //notch
                0, 1, -whitekeylen,
                0, 0.95, -whitekeylen - 0.1,
                1, 0.95, -whitekeylen - 0.1,
                1, 1, -whitekeylen,

                0, 0.85, -whitekeylen - 0.07,
                0, 0.95, -whitekeylen - 0.1,
                1, 0.95, -whitekeylen - 0.1,
                1, 0.85, -whitekeylen - 0.07,
                //left
                0, 1, 0,
                0, 1, -whitekeylen,
                0, 0, -whitekeylen,
                0, 0, 0,
                //right
                1, 1, 0,
                1, 1, -whitekeylen,
                1, 0, -whitekeylen,
                1, 0, 0,
            };

            float[] cols = new float[] {
                //front
                0.8f,
                0.8f,
                0.8f,
                0.8f,
                //front dark
                0.6f,
                0.6f,
                0.6f,
                0.6f,
                //top
                1,
                1,
                1,
                1,
                //notch
                1f,
                0.9f,
                0.9f,
                1f,

                0.9f,
                0.9f,
                0.9f,
                0.9f,
                //left
                0.6f,
                0.6f,
                0.6f,
                0.6f,
                //right
                0.6f,
                0.6f,
                0.6f,
                0.6f,
            };
            float[] blend = new float[] {
                //front
                1, 1, 1, 1,
                //front dark
                1, 1, 1, 1,
                //top
                0, 1, 1, 0,
                //notch
                1, 1, 1, 1,

                1, 1, 1, 1,
                //left
                0, 1, 1, 0,
                //right
                0, 1, 1, 0,
            };

            int[] indexes = new int[28];
            for (int i = 0; i < indexes.Length; i++) indexes[i] = i;
            whiteKeyBufferLen = indexes.Length;

            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyVert);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(verts.Length * 8),
                verts,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyCol);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(cols.Length * 4),
                cols,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyBlend);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(blend.Length * 4),
                blend,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, whiteKeyIndx);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                (IntPtr)(indexes.Length * 4),
                indexes,
                BufferUsageHint.StaticDraw);
            #endregion

            #region Black Key Model
            float blackkeylen = 7.5f;
            verts = new double[] {
                //front
                0, 0, -blackkeylen,
                0, 1, -blackkeylen + 1,
                1, 1, -blackkeylen + 1,
                1, 0, -blackkeylen,
                //top
                0, 1, -blackkeylen + 1,
                0, 1, -0,
                1, 1, -0,
                1, 1, -blackkeylen + 1,
                //left
                0, 0, 0,
                0, 0, -blackkeylen,
                0, 1, -blackkeylen + 1,
                0, 1, 0,
                //left
                1, 0, 0,
                1, 0, -blackkeylen,
                1, 1, -blackkeylen + 1,
                1, 1, 0,
            };

            cols = new float[] {
                //front
                0.9f,
                0.95f,
                0.95f,
                0.9f,                
                //top
                1f,
                0.94f,
                0.94f,
                1f,              
                //left
                0.8f,
                0.8f,
                0.9f,
                0.8f,        
                //right
                0.8f,
                0.8f,
                0.9f,
                0.8f,
            };
            blend = new float[] {
                //front
                1, 1, 1, 1,
                //front
                1, 0, 0, 1,
                //left
                0, 1, 1, 0,
                //right
                0, 1, 1, 0,
            };

            indexes = new int[16];
            for (int i = 0; i < indexes.Length; i++) indexes[i] = i;
            blackKeyBufferLen = indexes.Length;

            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyVert);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(verts.Length * 8),
                verts,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyCol);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(cols.Length * 4),
                cols,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyBlend);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(blend.Length * 4),
                blend,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, blackKeyIndx);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                (IntPtr)(indexes.Length * 4),
                indexes,
                BufferUsageHint.StaticDraw);
            #endregion
        }

        Color4[] keyColors = new Color4[514];
        double[] x1array = new double[257];
        double[] wdtharray = new double[257];
        double[] keyPressFactor = new double[257];
        public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff)
        {
            GL.Enable(EnableCap.Blend);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.ColorArray);
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Always);

            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, buffer3dbuf);
            GL.Viewport(0, 0, renderSettings.width, renderSettings.height);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            long nc = 0;
            int firstNote = settings.firstNote;
            int lastNote = settings.lastNote;
            bool sameWidth = settings.sameWidthNotes;
            double deltaTimeOnScreen = NoteScreenTime;
            double noteDownSpeed = settings.noteDownSpeed;
            double noteUpSpeed = settings.noteUpSpeed;
            bool blockNotes = settings.boxNotes;
            bool useVel = settings.useVel;

            double fov = settings.FOV;
            double aspect = (double)renderSettings.width / renderSettings.height;
            double viewdist = settings.viewdist;
            double viewheight = settings.viewHeight;
            double viewoffset = -settings.viewOffset;
            double camAng = settings.camAng;
            fov /= 1;
            for (int i = 0; i < 514; i++) keyColors[i] = Color4.Transparent;
            for (int i = 0; i < keyPressFactor.Length; i++) keyPressFactor[i] = Math.Max(keyPressFactor[i] / 1.05 - noteUpSpeed, 0);
            float wdth;
            double wdthd;
            float wdth2;
            float r, g, b, a, r2, g2, b2, a2;
            float x1;
            float x2;
            double x1d;
            double x2d;
            double y1;
            double y2;
            Matrix4 mvp;
            if (settings.sameWidthNotes)
            {
                for (int i = 0; i < 257; i++)
                {
                    x1array[i] = (float)(i - firstNote) / (lastNote - firstNote);
                    wdtharray[i] = 1.0f / (lastNote - firstNote);
                }
            }
            else
            {
                double knmfn = keynum[firstNote];
                double knmln = keynum[lastNote - 1];
                if (blackKeys[firstNote]) knmfn = keynum[firstNote - 1] + 0.5;
                if (blackKeys[lastNote - 1]) knmln = keynum[lastNote] - 0.5;
                for (int i = 0; i < 257; i++)
                {
                    if (!blackKeys[i])
                    {
                        x1array[i] = (float)(keynum[i] - knmfn) / (knmln - knmfn + 1);
                        wdtharray[i] = 1.0f / (knmln - knmfn + 1);
                    }
                    else
                    {
                        int _i = i + 1;
                        wdth = (float)(0.6f / (knmln - knmfn + 1));
                        int bknum = keynum[i] % 5;
                        double offset = wdth / 2;
                        if (bknum == 0 || bknum == 2)
                        {
                            offset *= 1.3;
                        }
                        else if (bknum == 1 || bknum == 4)
                        {
                            offset *= 0.7;
                        }
                        x1array[i] = (float)(keynum[_i] - knmfn) / (knmln - knmfn + 1) - offset;
                        wdtharray[i] = wdth;
                    }
                }
            }

            #region Notes
            noteBuffPos = 0;
            GL.UseProgram(noteShader);

            mvp = Matrix4.Identity *
                Matrix4.CreateTranslation(0, -(float)viewheight, -(float)viewoffset) *
                Matrix4.CreateScale(1, 1, -1) *
                Matrix4.CreateRotationX((float)camAng) *
                Matrix4.CreatePerspectiveFieldOfView((float)fov, (float)aspect, 0.01f, 400)
                ;
            GL.UniformMatrix4(uNoteMVP, false, ref mvp);

            double renderCutoff = midiTime + deltaTimeOnScreen;
            double renderStart = midiTime + NoteCollectorOffset;

            if (blockNotes)
            {
                foreach (Note n in notes)
                {
                    if (n.end >= renderStart || !n.hasEnded)
                    {
                        if (n.start < renderCutoff)
                        {
                            nc++;
                            int k = n.note;
                            if (!(k >= firstNote && k < lastNote)) continue;
                            Color4 coll = n.track.trkColor[n.channel * 2];
                            Color4 colr = n.track.trkColor[n.channel * 2 + 1];
                            float shade = 0;
                            if (n.start < midiTime && (n.end > midiTime || !n.hasEnded))
                            {
                                if (!n.hasEnded)
                                    shade = 0.3f;
                                else
                                {
                                    double len = n.end - n.start;
                                    double offset = n.end - midiTime;
                                    shade = (float)(Math.Pow(offset / len, 0.4) * 0.3);
                                }
                            }
                            shade -= 0.3f;
                            x1d = x1array[k] - 0.5;
                            wdthd = wdtharray[k];
                            y1 = n.end - midiTime;
                            y2 = n.start - midiTime;
                            if (!n.hasEnded)
                                y1 = viewdist * deltaTimeOnScreen;
                            y1 /= deltaTimeOnScreen / viewdist;
                            y2 /= deltaTimeOnScreen / viewdist;

                            if (x1d < 0) x1d += wdthd;

                            r = coll.R;
                            g = coll.G;
                            b = coll.B;
                            a = coll.A;
                            r2 = colr.R;
                            g2 = colr.G;
                            b2 = colr.B;
                            a2 = colr.A;

                            int pos = noteBuffPos * 12;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = 0;
                            noteVertBuff[pos++] = y2;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = 0;
                            noteVertBuff[pos++] = y1;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = -wdthd;
                            noteVertBuff[pos++] = y1;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = -wdthd;
                            noteVertBuff[pos++] = y2;

                            pos = noteBuffPos * 16;
                            noteColBuff[pos++] = r;
                            noteColBuff[pos++] = g;
                            noteColBuff[pos++] = b;
                            noteColBuff[pos++] = a;
                            noteColBuff[pos++] = r;
                            noteColBuff[pos++] = g;
                            noteColBuff[pos++] = b;
                            noteColBuff[pos++] = a;
                            noteColBuff[pos++] = r2;
                            noteColBuff[pos++] = g2;
                            noteColBuff[pos++] = b2;
                            noteColBuff[pos++] = a2;
                            noteColBuff[pos++] = r2;
                            noteColBuff[pos++] = g2;
                            noteColBuff[pos++] = b2;
                            noteColBuff[pos++] = a2;

                            pos = noteBuffPos * 4;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;

                            noteBuffPos++;
                            FlushNoteBuffer();

                        }
                        else break;
                    }
                }

                FlushNoteBuffer(false);

                foreach (Note n in notes)
                {
                    if (n.end >= renderStart || !n.hasEnded)
                    {
                        if (n.start < renderCutoff)
                        {
                            nc++;
                            int k = n.note;
                            if (!(k >= firstNote && k < lastNote)) continue;
                            Color4 coll = n.track.trkColor[n.channel * 2];
                            Color4 colr = n.track.trkColor[n.channel * 2 + 1];
                            float shade = 0;
                            if (n.start < midiTime && (n.end > midiTime || !n.hasEnded))
                            {
                                if (!n.hasEnded)
                                    shade = 0.3f;
                                else
                                {
                                    double len = n.end - n.start;
                                    double offset = n.end - midiTime;
                                    shade = (float)(Math.Pow(offset / len, 0.4) * 0.3);
                                }
                            }
                            shade -= 0.2f;
                            x1d = x1array[k] - 0.5;
                            wdthd = wdtharray[k];
                            x2d = x1d + wdthd;
                            y1 = n.end - midiTime;
                            y2 = n.start - midiTime;
                            if (!n.hasEnded)
                                y1 = viewdist * deltaTimeOnScreen;
                            y1 /= deltaTimeOnScreen / viewdist;
                            y2 /= deltaTimeOnScreen / viewdist;

                            r = coll.R;
                            g = coll.G;
                            b = coll.B;
                            a = coll.A;
                            r2 = colr.R;
                            g2 = colr.G;
                            b2 = colr.B;
                            a2 = colr.A;

                            int pos = noteBuffPos * 12;
                            noteVertBuff[pos++] = x2d;
                            noteVertBuff[pos++] = -wdthd;
                            noteVertBuff[pos++] = y2;
                            noteVertBuff[pos++] = x2d;
                            noteVertBuff[pos++] = 0;
                            noteVertBuff[pos++] = y2;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = 0;
                            noteVertBuff[pos++] = y2;
                            noteVertBuff[pos++] = x1d;
                            noteVertBuff[pos++] = -wdthd;
                            noteVertBuff[pos++] = y2;

                            pos = noteBuffPos * 16;
                            noteColBuff[pos++] = r;
                            noteColBuff[pos++] = g;
                            noteColBuff[pos++] = b;
                            noteColBuff[pos++] = a;
                            noteColBuff[pos++] = r;
                            noteColBuff[pos++] = g;
                            noteColBuff[pos++] = b;
                            noteColBuff[pos++] = a;
                            noteColBuff[pos++] = r2;
                            noteColBuff[pos++] = g2;
                            noteColBuff[pos++] = b2;
                            noteColBuff[pos++] = a2;
                            noteColBuff[pos++] = r2;
                            noteColBuff[pos++] = g2;
                            noteColBuff[pos++] = b2;
                            noteColBuff[pos++] = a2;

                            pos = noteBuffPos * 4;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;
                            noteShadeBuff[pos++] = shade;

                            noteBuffPos++;
                            FlushNoteBuffer();

                        }
                        else break;
                    }
                }
            }

            foreach (Note n in notes)
            {
                if (n.end >= renderStart || !n.hasEnded)
                {
                    if (n.start < renderCutoff)
                    {
                        nc++;
                        int k = n.note;
                        if (!(k >= firstNote && k < lastNote)) continue;
                        Color4 coll = n.track.trkColor[n.channel * 2];
                        Color4 colr = n.track.trkColor[n.channel * 2 + 1];
                        float shade = 0;
                        if (n.start < midiTime && (n.end > midiTime || !n.hasEnded))
                        {
                            Color4 origcoll = keyColors[k * 2];
                            Color4 origcolr = keyColors[k * 2 + 1];
                            float blendfac = coll.A * 0.8f;
                            float revblendfac = 1 - blendfac;
                            keyColors[k * 2] = new Color4(
                                coll.R * blendfac + origcoll.R * revblendfac,
                                coll.G * blendfac + origcoll.G * revblendfac,
                                coll.B * blendfac + origcoll.B * revblendfac,
                                1);
                            blendfac = colr.A * 0.8f;
                            revblendfac = 1 - blendfac;
                            keyColors[k * 2 + 1] = new Color4(
                                colr.R * blendfac + origcolr.R * revblendfac,
                                colr.G * blendfac + origcolr.G * revblendfac,
                                colr.B * blendfac + origcolr.B * revblendfac,
                                1);
                            if (useVel)
                                keyPressFactor[k] = Math.Min(1, keyPressFactor[k] + noteDownSpeed * n.vel / 127.0);
                            else
                                keyPressFactor[k] = Math.Min(1, keyPressFactor[k] + noteDownSpeed);
                            if (!n.hasEnded)
                                shade = 0.3f;
                            else
                            {
                                double len = n.end - n.start;
                                double offset = n.end - midiTime;
                                shade = (float)(Math.Pow(offset / len, 0.4) * 0.3);
                            }
                        }
                        x1d = x1array[k] - 0.5;
                        wdthd = wdtharray[k];
                        x2d = x1d + wdthd;
                        y1 = n.end - midiTime;
                        y2 = n.start - midiTime;
                        if (!n.hasEnded)
                            y1 = viewdist * deltaTimeOnScreen;
                        y1 /= deltaTimeOnScreen / viewdist;
                        y2 /= deltaTimeOnScreen / viewdist;

                        r = coll.R;
                        g = coll.G;
                        b = coll.B;
                        a = coll.A;
                        r2 = colr.R;
                        g2 = colr.G;
                        b2 = colr.B;
                        a2 = colr.A;

                        int pos = noteBuffPos * 12;
                        noteVertBuff[pos++] = x2d;
                        noteVertBuff[pos++] = 0;
                        noteVertBuff[pos++] = y2;
                        noteVertBuff[pos++] = x2d;
                        noteVertBuff[pos++] = 0;
                        noteVertBuff[pos++] = y1;
                        noteVertBuff[pos++] = x1d;
                        noteVertBuff[pos++] = 0;
                        noteVertBuff[pos++] = y1;
                        noteVertBuff[pos++] = x1d;
                        noteVertBuff[pos++] = 0;
                        noteVertBuff[pos++] = y2;

                        pos = noteBuffPos * 16;
                        noteColBuff[pos++] = r;
                        noteColBuff[pos++] = g;
                        noteColBuff[pos++] = b;
                        noteColBuff[pos++] = a;
                        noteColBuff[pos++] = r;
                        noteColBuff[pos++] = g;
                        noteColBuff[pos++] = b;
                        noteColBuff[pos++] = a;
                        noteColBuff[pos++] = r2;
                        noteColBuff[pos++] = g2;
                        noteColBuff[pos++] = b2;
                        noteColBuff[pos++] = a2;
                        noteColBuff[pos++] = r2;
                        noteColBuff[pos++] = g2;
                        noteColBuff[pos++] = b2;
                        noteColBuff[pos++] = a2;

                        pos = noteBuffPos * 4;
                        noteShadeBuff[pos++] = shade;
                        noteShadeBuff[pos++] = shade;
                        noteShadeBuff[pos++] = shade;
                        noteShadeBuff[pos++] = shade;

                        noteBuffPos++;
                        FlushNoteBuffer();

                    }
                    else break;
                }
            }

            FlushNoteBuffer(false);
            noteBuffPos = 0;
            GL.DepthFunc(DepthFunction.Less);

            LastNoteCount = nc;
            #endregion

            #region Keyboard
            Color4[] origColors = new Color4[257];
            for (int k = firstNote; k < lastNote; k++)
            {
                if (isBlackNote(k))
                    origColors[k] = Color4.Black;
                else
                    origColors[k] = Color4.White;
            }

            GL.UseProgram(whiteKeyShader);

            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyVert);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Double, false, 24, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyCol);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyBlend);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, whiteKeyIndx);
            GL.IndexPointer(IndexPointerType.Int, 1, 0);

            for (int n = firstNote; n < lastNote; n++)
            {
                x1 = (float)x1array[n];
                wdth = (float)wdtharray[n];
                x2 = x1 + wdth;

                if (!blackKeys[n])
                {
                    y2 = 0;
                    if (settings.sameWidthNotes)
                    {
                        int _n = n % 12;
                        if (_n == 0)
                            x2 += wdth * 0.666f;
                        else if (_n == 2)
                        {
                            x1 -= wdth / 3;
                            x2 += wdth / 3;
                        }
                        else if (_n == 4)
                            x1 -= wdth / 3 * 2;
                        else if (_n == 5)
                            x2 += wdth * 0.75f;
                        else if (_n == 7)
                        {
                            x1 -= wdth / 4;
                            x2 += wdth / 2;
                        }
                        else if (_n == 9)
                        {
                            x1 -= wdth / 2;
                            x2 += wdth / 4;
                        }
                        else if (_n == 11)
                            x1 -= wdth * 0.75f;
                        wdth2 = wdth * 2;
                    }
                    else
                    {
                        wdth2 = wdth;
                    }
                }
                else continue;
                wdth = x2 - x1;
                x1 -= 0.5f;

                var coll = keyColors[n * 2];
                var colr = keyColors[n * 2 + 1];
                var origcol = origColors[n];
                float blendfac = coll.A * 0.8f;
                float revblendfac = 1 - blendfac;
                coll = new Color4(
                    coll.R * blendfac + origcol.R * revblendfac,
                    coll.G * blendfac + origcol.G * revblendfac,
                    coll.B * blendfac + origcol.B * revblendfac,
                    1);
                blendfac = colr.A * 0.8f;
                revblendfac = 1 - blendfac;
                colr = new Color4(
                    colr.R * blendfac + origcol.R * revblendfac,
                    colr.G * blendfac + origcol.G * revblendfac,
                    colr.B * blendfac + origcol.B * revblendfac,
                    1);

                GL.Uniform4(uWhiteKeycoll, coll);
                GL.Uniform4(uWhiteKeycolr, colr);

                mvp = Matrix4.Identity *
                    Matrix4.CreateScale(0.95f, 1, 1) *
                    Matrix4.CreateTranslation(0, (float)-keyPressFactor[n] / 2 - 0.3f, 0) *
                    Matrix4.CreateScale(wdth, wdth2, wdth2) *
                    Matrix4.CreateTranslation(x1, 0, 0) *
                    Matrix4.CreateTranslation(0, -(float)viewheight, -(float)viewoffset) *
                    Matrix4.CreateScale(1, 1, -1) *
                    Matrix4.CreateRotationX((float)camAng) *
                    Matrix4.CreatePerspectiveFieldOfView((float)fov, (float)aspect, 0.01f, 400)
                    ;

                GL.UniformMatrix4(uWhiteKeyMVP, false, ref mvp);
                GL.DrawElements(PrimitiveType.Quads, whiteKeyBufferLen, DrawElementsType.UnsignedInt, IntPtr.Zero);
            }

            GL.UseProgram(blackKeyShader);

            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyVert);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Double, false, 24, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyCol);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyBlend);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, blackKeyIndx);
            GL.IndexPointer(IndexPointerType.Int, 1, 0);

            for (int n = firstNote; n < lastNote; n++)
            {
                x1 = (float)x1array[n];
                wdth = (float)wdtharray[n];
                x1 -= 0.5f;

                if (!blackKeys[n]) continue;

                var coll = keyColors[n * 2];
                var colr = keyColors[n * 2 + 1];
                var origcol = origColors[n];
                float blendfac = coll.A * 0.8f;
                float revblendfac = 1 - blendfac;
                coll = new Color4(
                    coll.R * blendfac + origcol.R * revblendfac,
                    coll.G * blendfac + origcol.G * revblendfac,
                    coll.B * blendfac + origcol.B * revblendfac,
                    1);
                blendfac = colr.A * 0.8f;
                revblendfac = 1 - blendfac;
                colr = new Color4(
                    colr.R * blendfac + origcol.R * revblendfac,
                    colr.G * blendfac + origcol.G * revblendfac,
                    colr.B * blendfac + origcol.B * revblendfac,
                    1);

                GL.Uniform4(uBlackKeycoll, coll);
                GL.Uniform4(uBlackKeycolr, colr);

                mvp = Matrix4.Identity *
                    Matrix4.CreateScale(0.95f, 1, 1) *
                    Matrix4.CreateTranslation(0, 2, 0) *
                    Matrix4.CreateTranslation(0, (float)-keyPressFactor[n] / 2 - 0.3f, 0) *
                    Matrix4.CreateScale(wdth, wdth, wdth) *
                    Matrix4.CreateTranslation(x1, 0, 0) *
                    Matrix4.CreateTranslation(0, -(float)viewheight, -(float)viewoffset) *
                    Matrix4.CreateScale(1, 1, -1) *
                    Matrix4.CreateRotationX((float)camAng) *
                    Matrix4.CreatePerspectiveFieldOfView((float)fov, (float)aspect, 0.01f, 400)
                    ;

                GL.UniformMatrix4(uWhiteKeyMVP, false, ref mvp);
                GL.DrawElements(PrimitiveType.Quads, blackKeyBufferLen, DrawElementsType.UnsignedInt, IntPtr.Zero);
            }
            #endregion

            GL.Disable(EnableCap.Blend);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.ColorArray);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);

            GL.DisableVertexAttribArray(0);
            GL.DisableVertexAttribArray(1);
            GL.DisableVertexAttribArray(2);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, finalCompositeBuff);
            GL.BindTexture(TextureTarget.Texture2D, buffer3dtex);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            util.DrawScreenQuad();
        }

        public void SetTrackColors(Color4[][] trakcs)
        {
            for (int i = 0; i < trakcs.Length; i++)
            {
                for (int j = 0; j < trakcs[i].Length / 2; j++)
                {
                    trakcs[i][j * 2] = Color4.FromHsv(new OpenTK.Vector4((i * 16 + j) * 1.36271f % 1, 1.0f, 1, 1f));
                    trakcs[i][j * 2 + 1] = Color4.FromHsv(new OpenTK.Vector4((i * 16 + j) * 1.36271f % 1, 1.0f, 1, 1f));
                }
            }
        }

        void FlushNoteBuffer(bool check = true)
        {
            if (noteBuffPos < noteBuffLen && check) return;
            if (noteBuffPos == 0) return;
            GL.BindBuffer(BufferTarget.ArrayBuffer, noteVert);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(noteVertBuff.Length * 8),
                noteVertBuff,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Double, false, 24, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, noteCol);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(noteColBuff.Length * 4),
                noteColBuff,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 16, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, noteShade);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(noteShadeBuff.Length * 4),
                noteShadeBuff,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, noteIndx);
            GL.IndexPointer(IndexPointerType.Int, 1, 0);
            GL.DrawElements(PrimitiveType.Quads, noteBuffPos * 4, DrawElementsType.UnsignedInt, IntPtr.Zero);
            noteBuffPos = 0;
        }

        bool isBlackNote(int n)
        {
            n = n % 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }
    }
}
