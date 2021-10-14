using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;
using Type = System.Type;

namespace UnityEditor.VFX
{
    abstract class VFXBlock : VFXSlotContainerModel<VFXContext, VFXModel>
    {
        public VFXBlock()
        {
            m_UICollapsed = false;
        }

        public static T CreateImplicitBlock<T>(VFXData data) where T : VFXBlock
        {
            var block = ScriptableObject.CreateInstance<T>();
            block.m_TransientData = data;
            return block;
        }

        private VFXData m_TransientData = null;

        // Deprecated. But has to keep around it to initialize slot field 
        [SerializeField]
        private bool m_Disabled = false;

        [SerializeField]
        private VFXSlot m_ActivationSlot;
        public override VFXSlot activationSlot => m_ActivationSlot;

        private bool m_CachedEnableState = true;
        private bool m_EnableStateUpToDate = false;
        public bool enabled
        {
            get
            {
                if (!m_EnableStateUpToDate)
                    UpdateEnableState();

                return m_CachedEnableState;
            }
        }

        public virtual bool isValid
        {
            get
            {
                if (GetParent() == null) return true; // a block is invalid only if added to incompatible context.
                if ((compatibleContexts & GetParent().contextType) != GetParent().contextType)
                    return false;
                if (GetParent() is VFXBlockSubgraphContext subgraphContext)
                    return (subgraphContext.compatibleContextType & compatibleContexts) == subgraphContext.compatibleContextType;

                return true;
            }
        }

        public bool isActive
        {
            get { return enabled && isValid; }
        }

        public abstract VFXContextType compatibleContexts { get; }
        public abstract VFXDataType compatibleData { get; }
        public virtual IEnumerable<VFXAttributeInfo> attributes { get { return Enumerable.Empty<VFXAttributeInfo>(); } }
        public virtual IEnumerable<VFXNamedExpression> parameters { get { return GetExpressionsFromSlots(this); } }
        public VFXExpression activationExpression => m_ActivationSlot.GetExpression();
        public virtual IEnumerable<string> includes { get { return Enumerable.Empty<string>(); } }
        public virtual string source { get { return null; } }

        public override void OnEnable()
        {
            base.OnEnable();

            if (m_ActivationSlot == null || m_ActivationSlot.name != "_vfx_enabled")
            {
                var oldSlot = m_ActivationSlot;

                var prop = new VFXPropertyWithValue(new VFXProperty(typeof(bool), "_vfx_enabled"), !m_Disabled);
                m_ActivationSlot = VFXSlot.Create(prop, VFXSlot.Direction.kInput);
                m_ActivationSlot.SetOwner(this);

                if (oldSlot != null)
                {
                    VFXSlot.CopyLinksAndValue(m_ActivationSlot, oldSlot, false);
                    oldSlot.UnlinkAll(false, false);
                    DestroyImmediate(oldSlot);
                }
            }
        }

        private void UpdateEnableState()
        {
            // fast path for not connected activation slots
            if (!activationSlot.HasLink())
                m_CachedEnableState = (bool)activationSlot.value;
            else
            {
                var enableExp = activationSlot.GetExpression();

                var context = new VFXExpression.Context(VFXExpressionContextOption.CPUEvaluation);
                context.RegisterExpression(enableExp);
                context.Compile();

                enableExp = context.GetReduced(enableExp);
                if (enableExp.Is(VFXExpression.Flags.Value))
                    m_CachedEnableState = enableExp.Get<bool>();
                else
                    m_CachedEnableState = true;

                Debug.Log("RECOMPUTE ENABLE STATE FROM GRAPH: " + m_CachedEnableState + " " + enableExp);
            }
       
            m_EnableStateUpToDate = true;
        }

        protected override void OnInvalidate(VFXModel model, InvalidationCause cause)
        {
            base.OnInvalidate(model, cause);
            if (model == activationSlot &&
               (cause == InvalidationCause.kParamChanged || // This does not account for param/connection changed upstream
                cause == InvalidationCause.kConnectionChanged ||
                cause == InvalidationCause.kExpressionInvalidated ||
                cause == InvalidationCause.kExpressionValueInvalidated))
            {
                Debug.Log("ENABLE SLOT CHANGED " + cause);

                bool oldEnabledStateValid = m_EnableStateUpToDate;
                bool oldEnableState = m_CachedEnableState;
                UpdateEnableState();

                if (!oldEnabledStateValid || (m_CachedEnableState != oldEnableState))
                    Invalidate(InvalidationCause.kEnableChanged);
            }
        }

        public override void CollectDependencies(HashSet<ScriptableObject> objs, bool ownedOnly = true)
        {
            base.CollectDependencies(objs, ownedOnly);
            objs.Add(m_ActivationSlot);
            m_ActivationSlot.CollectDependencies(objs, ownedOnly);
        }

        public IEnumerable<VFXAttributeInfo> mergedAttributes
        {
            get
            {
                var attribs = new Dictionary<VFXAttribute, VFXAttributeMode>();
                foreach (var a in attributes)
                {
                    VFXAttributeMode mode = VFXAttributeMode.None;
                    attribs.TryGetValue(a.attrib, out mode);
                    mode |= a.mode;
                    attribs[a.attrib] = mode;
                }
                return attribs.Select(kvp => new VFXAttributeInfo(kvp.Key, kvp.Value));
            }
        }

        public VFXData GetData()
        {
            if (GetParent() != null)
                return GetParent().GetData();
            return m_TransientData;
        }

        public sealed override VFXCoordinateSpace GetOutputSpaceFromSlot(VFXSlot slot)
        {
            /* For block, space is directly inherited from parent context, this method should remains sealed */
            if (GetParent() != null)
                return GetParent().space;
            return (VFXCoordinateSpace)int.MaxValue;
        }

        public VFXContext flattenedParent
        {
            get
            {
                return m_FlattenedParent != null ? m_FlattenedParent : GetParent();
            }
            set
            {
                m_FlattenedParent = value;
            }
        }

        private VFXContext m_FlattenedParent;
    }
}
