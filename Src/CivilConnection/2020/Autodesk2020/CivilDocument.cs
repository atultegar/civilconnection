@@ -66,10 +66,6 @@ namespace CivilConnection
        /// The internal element.
        /// </value>
        internal object InternalElement { get { return this._document; } }
        /// <summary>
        /// Surfaces
        /// </summary>
        private AeccSurfaces _surfaces;
        #endregion

        #region CONSTRUCTORS
@ -82,7 +78,6 @@ namespace CivilConnection
            this._document = _doc;
            _corridors = _doc.Corridors;
            _alignments = _doc.AlignmentsSiteless;
            _surfaces = _doc.Surfaces;
        }
        #endregion

@ -245,37 +240,6 @@ namespace CivilConnection
            return this.GetAlignments().First(x => x.Name == name);
        }

        /// <summary>
        /// Gets all surfaces
        /// </summary>
        /// <returns>
        /// List of surfaces
        /// </returns>
        public IList<CivilSurface> GetSurfaces()
        {
            Utils.Log(string.Format("CivilDocument.GetSurfaces started...", ""));

            IList<CivilSurface> output = new List<CivilSurface>();
            foreach (AeccSurface s in this._surfaces)
            {
                output.Add(new CivilSurface(s));
            }

            Utils.Log(string.Format("CivilDocument.GetSurfaces completed.", ""));

            return output;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public CivilSurface GetSurfaceByName(string name)
        {
            return this.GetSurfaces().First(x => x.Name == name);
        }

        #region AUTOCAD METHODS
        /// <summary>
        /// Adds the arc to the document.