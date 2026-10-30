using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Gridfinity.Sw
{
    /// <summary>
    /// Wraps an object so a PropertyGrid shows only the properties in the chosen categories. Edits still go to the
    /// original object, which lets the Box and Cutout tabs share one tray definition.
    /// </summary>
    internal sealed class CategoryView : ICustomTypeDescriptor
    {
        private readonly object _target;
        private readonly HashSet<string> _categories;

        public CategoryView(object target, params string[] categories)
        {
            _target = target;
            _categories = new HashSet<string>(categories);
        }

        public AttributeCollection GetAttributes() { return TypeDescriptor.GetAttributes(_target, true); }
        public string GetClassName() { return TypeDescriptor.GetClassName(_target, true); }
        public string GetComponentName() { return TypeDescriptor.GetComponentName(_target, true); }
        public TypeConverter GetConverter() { return TypeDescriptor.GetConverter(_target, true); }
        public EventDescriptor GetDefaultEvent() { return TypeDescriptor.GetDefaultEvent(_target, true); }
        public PropertyDescriptor GetDefaultProperty() { return TypeDescriptor.GetDefaultProperty(_target, true); }
        public object GetEditor(Type editorBaseType) { return TypeDescriptor.GetEditor(_target, editorBaseType, true); }
        public EventDescriptorCollection GetEvents() { return TypeDescriptor.GetEvents(_target, true); }
        public EventDescriptorCollection GetEvents(Attribute[] attributes) { return TypeDescriptor.GetEvents(_target, attributes, true); }
        public PropertyDescriptorCollection GetProperties() { return GetProperties(null); }

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection all = attributes == null
                ? TypeDescriptor.GetProperties(_target, true)
                : TypeDescriptor.GetProperties(_target, attributes, true);

            var kept = new List<PropertyDescriptor>();
            foreach (PropertyDescriptor p in all)
                if (_categories.Contains(p.Category)) kept.Add(p);
            return new PropertyDescriptorCollection(kept.ToArray());
        }

        public object GetPropertyOwner(PropertyDescriptor pd) { return _target; }
    }
}
