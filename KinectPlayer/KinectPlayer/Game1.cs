using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;

using Microsoft.Kinect;

namespace KinectPlayer
{
    /// <summary>
    /// This is the main type for your game
    /// </summary>
    public class Game1 : Microsoft.Xna.Framework.Game
    {
        KinectSensor myKinect;

        Rectangle fullScreenRectangle;

        string errorMessage = "";

        protected bool setupKinect()
        {
            // Check to see if a Kinect is available
            if (KinectSensor.KinectSensors.Count == 0)
            {
                errorMessage = "No Kinects detected";
                return false;
            }

            // Get the first Kinect on the computer
            myKinect = KinectSensor.KinectSensors[0];

            // Start the Kinect running and select all the streams
            try
            {
                myKinect.SkeletonStream.Enable();
                myKinect.ColorStream.Enable();
                myKinect.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
                myKinect.Start();
            }
            catch
            {
                errorMessage = "Kinect initialise failed";
                return false;
            }

            // connect a handler to the event that fires when new frames are available

            myKinect.AllFramesReady += new EventHandler<AllFramesReadyEventArgs>(myKinect_AllFramesReady);

            return true;
        }

        byte[] colorData = null;
        short[] depthData = null;

        Texture2D gameMaskTexture = null;
        Texture2D kinectVideoTexture;

        Texture2D gameImageTexture;
        Color[] maskImageColors = null;

        Skeleton[] skeletons = null;
        Skeleton activeSkeleton = null;

        int activeSkeletonNumber;

        void myKinect_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            // Puts a copy of the video image into the kinect video texture
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame == null)
                    return;

                if (colorData == null)
                    colorData = new byte[colorFrame.Width * colorFrame.Height * 4];

                colorFrame.CopyPixelDataTo(colorData);

                kinectVideoTexture = new Texture2D(GraphicsDevice, colorFrame.Width, colorFrame.Height);

                Color[] bitmap = new Color[colorFrame.Width * colorFrame.Height];

                int sourceOffset = 0;

                for (int i = 0; i < bitmap.Length; i++)
                {
                    bitmap[i] = new Color(colorData[sourceOffset + 2],
                        colorData[sourceOffset + 1], colorData[sourceOffset], 255);
                    sourceOffset += 4;
                }

                kinectVideoTexture.SetData(bitmap);
            }

            // Finds the currently active skeleton
            using (SkeletonFrame frame = e.OpenSkeletonFrame())
            {
                if (frame == null)
                    return;

                skeletons = new Skeleton[frame.SkeletonArrayLength];
                frame.CopySkeletonDataTo(skeletons);
            }

            activeSkeletonNumber = 0;

            for (int i = 0; i < skeletons.Length; i++)
            {
                if (skeletons[i].TrackingState == SkeletonTrackingState.Tracked)
                {
                    activeSkeletonNumber = i + 1;
                    activeSkeleton = skeletons[i];
                    break;
                }
            }

            // Creates a game background image with transparent regions 
            // where the player is displayed
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                // Get the depth data
                if (depthFrame == null) 
                    return;

                if (depthData == null)
                    depthData = new short[depthFrame.Width * depthFrame.Height];

                depthFrame.CopyPixelDataTo(depthData);

                // Create the mask from the background image
                gameImageTexture.GetData(maskImageColors);

                if (activeSkeletonNumber != 0)
                {
                    for (int depthPos = 0; depthPos < depthData.Length; depthPos++)
                    {
                        // find a background to mask - split off bottom bits
                        int playerNo = depthData[depthPos] & 0x07;

                        if (playerNo != activeSkeletonNumber)
                        {
                            // We have a player to mask

                            // find the X and Y positions of the depth point
                            int x = depthPos % depthFrame.Width;
                            int y = depthPos / depthFrame.Width;

                            // get the X and Y positions in the video feed
                            ColorImagePoint playerPoint = myKinect.MapDepthToColorImagePoint(
                                DepthImageFormat.Resolution320x240Fps30, x, y, depthData[depthPos], ColorImageFormat.RgbResolution640x480Fps30);

                            // Map the player coordinates into our lower resolution background
                            // Have to do this because the lowest resultion for the color camera is 640x480

                            playerPoint.X /= 2;
                            playerPoint.Y /= 2;

                            // convert this into an offset into the mask color data
                            int gameImagePos = (playerPoint.X + (playerPoint.Y * depthFrame.Width));
                            if (gameImagePos < maskImageColors.Length)
                                // make this point in the mask transparent
                                maskImageColors[gameImagePos] = Color.FromNonPremultiplied(0, 0, 0, 0);
                        }
                    }
                }

                gameMaskTexture = new Texture2D(GraphicsDevice, depthFrame.Width, depthFrame.Height);
                gameMaskTexture.SetData(maskImageColors);
            }
        }

        Color boneColor = Color.White;

        Texture2D lineDot;

        void drawLine(Vector2 v1, Vector2 v2, Color col)
        {
            Vector2 origin = new Vector2(0.5f, 0.0f);
            Vector2 diff = v2 - v1;
            float angle;
            Vector2 scale = new Vector2(1.0f, diff.Length() / lineDot.Height);
            angle = (float)(Math.Atan2(diff.Y, diff.X)) - MathHelper.PiOver2;
            spriteBatch.Draw(lineDot, v1, null, col, angle, origin, scale, SpriteEffects.None, 1.0f);
        }

        void drawBone(Joint j1, Joint j2, Color col)
        {
            ColorImagePoint j1P = myKinect.MapSkeletonPointToColor(
                j1.Position,
                ColorImageFormat.RgbResolution640x480Fps30);
            Vector2 j1V = new Vector2(j1P.X, j1P.Y);

            ColorImagePoint j2P = myKinect.MapSkeletonPointToColor(
                j2.Position,
                ColorImageFormat.RgbResolution640x480Fps30);
            Vector2 j2V = new Vector2(j2P.X, j2P.Y);

            drawLine(j1V, j2V, col);
        }

        void drawSkeleton(Skeleton skel, Color col)
        {
            // Spine
            drawBone(skel.Joints[JointType.Head], skel.Joints[JointType.ShoulderCenter], col);
            drawBone(skel.Joints[JointType.ShoulderCenter], skel.Joints[JointType.Spine], col);

            // Left leg
            drawBone(skel.Joints[JointType.Spine], skel.Joints[JointType.HipCenter], col);
            drawBone(skel.Joints[JointType.HipCenter], skel.Joints[JointType.HipLeft], col);
            drawBone(skel.Joints[JointType.HipLeft], skel.Joints[JointType.KneeLeft], col);
            drawBone(skel.Joints[JointType.KneeLeft], skel.Joints[JointType.AnkleLeft], col);
            drawBone(skel.Joints[JointType.AnkleLeft], skel.Joints[JointType.FootLeft], col);

            // Right leg
            drawBone(skel.Joints[JointType.HipCenter], skel.Joints[JointType.HipRight], col);
            drawBone(skel.Joints[JointType.HipRight], skel.Joints[JointType.KneeRight], col);
            drawBone(skel.Joints[JointType.KneeRight], skel.Joints[JointType.AnkleRight], col);
            drawBone(skel.Joints[JointType.AnkleRight], skel.Joints[JointType.FootRight], col);

            // Left arm
            drawBone(skel.Joints[JointType.ShoulderCenter], skel.Joints[JointType.ShoulderLeft], col);
            drawBone(skel.Joints[JointType.ShoulderLeft], skel.Joints[JointType.ElbowLeft], col);
            drawBone(skel.Joints[JointType.ElbowLeft], skel.Joints[JointType.WristLeft], col);
            drawBone(skel.Joints[JointType.WristLeft], skel.Joints[JointType.HandLeft], col);

            // Right arm
            drawBone(skel.Joints[JointType.ShoulderCenter], skel.Joints[JointType.ShoulderRight], col);
            drawBone(skel.Joints[JointType.ShoulderRight], skel.Joints[JointType.ElbowRight], col);
            drawBone(skel.Joints[JointType.ElbowRight], skel.Joints[JointType.WristRight], col);
            drawBone(skel.Joints[JointType.WristRight], skel.Joints[JointType.HandRight], col);
        }
        
        GraphicsDeviceManager graphics;
        SpriteBatch spriteBatch;

        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            // Make the screen the same size as the video display output
            graphics.PreferredBackBufferHeight = 480;
            graphics.PreferredBackBufferWidth = 640;
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            // TODO: Add your initialization logic here
            setupKinect();

            fullScreenRectangle = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

            base.Initialize();
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            // TODO: use this.Content to load your game content here
            lineDot = Content.Load<Texture2D>("whiteDot");

            gameImageTexture = Content.Load<Texture2D>("CloudGameBackground");
            maskImageColors = new Color[gameImageTexture.Width * gameImageTexture.Height];
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            // TODO: Unload any non ContentManager content here
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            // Allows the game to exit
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                this.Exit();

            // TODO: Add your update logic here

            base.Update(gameTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // TODO: Add your drawing code here
            spriteBatch.Begin();

            if (activeSkeleton != null)
            {
                spriteBatch.Draw(gameMaskTexture, fullScreenRectangle, Color.White);

                drawSkeleton(activeSkeleton, Color.White);
            }

            spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
