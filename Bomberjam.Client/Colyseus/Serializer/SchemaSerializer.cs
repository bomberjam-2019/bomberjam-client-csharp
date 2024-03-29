using System;
using System.Reflection;
using Bomberjam.Client.Colyseus.Serializer.Schema;
using Type = System.Type;

namespace Bomberjam.Client.Colyseus.Serializer
{
    internal class SchemaSerializer<T> : ISerializer<T> // where T : Colyseus.Schema.Schema
    {
        protected T state;

        public SchemaSerializer()
        {
            state = Activator.CreateInstance<T>();
        }

        public void SetState(byte[] data)
        {
            (state as Schema.Schema).Decode(data);
        }

        public T GetState()
        {
            return state;
        }

        public void Patch(byte[] data)
        {
            (state as Schema.Schema).Decode(data);
        }

        public void Teardown()
        {
        }

        public void Handshake(byte[] bytes, int offset)
        {
            Type targetType = typeof(T);

            Type[] allTypes = targetType.Assembly.GetTypes();
            Type[] namespaceSchemaTypes = Array.FindAll(allTypes, t => (
                t.Namespace == targetType.Namespace &&
                typeof(Schema.Schema).IsAssignableFrom(targetType)
            ));

            Reflection reflection = new Reflection();
            Iterator it = new Iterator { Offset = offset };

            reflection.Decode(bytes, it);

            for (var i = 0; i < reflection.types.Count; i++)
            {
                Type schemaType = Array.Find(namespaceSchemaTypes, t => CompareTypes(t, reflection.types[i]));

                if (schemaType == null)
                {
                    throw new Exception("Local schema mismatch from server. Use \"schema-codegen\" to generate up-to-date local definitions.");
                }

                Context.GetInstance().SetTypeId(schemaType, reflection.types[i].id);
            }
        }

        private bool CompareTypes(Type schemaType, ReflectionType reflectionType)
        {
            FieldInfo[] fields = schemaType.GetFields();
            int typedFieldCount = 0;

            string fieldNames = "";
            for (var i = 0; i < fields.Length; i++)
            {
                fieldNames += fields[i].Name + ", ";
            }

            foreach (FieldInfo field in fields)
            {
                object[] typeAttributes = field.GetCustomAttributes(typeof(Schema.Type), true);

                if (typeAttributes.Length == 1)
                {
                    Schema.Type typedField = (Schema.Type)typeAttributes[0];
                    ReflectionField reflectionField = reflectionType.fields[typedField.Index];

                    if (
                        reflectionField == null ||
                        reflectionField.type.IndexOf(typedField.FieldType) != 0 ||
                        reflectionField.name != field.Name
                    )
                    {
                        return false;
                    }

                    typedFieldCount++;
                }
            }

            // skip if number of Type'd fields doesn't match
            if (typedFieldCount != reflectionType.fields.Count)
            {
                return false;
            }

            return true;
        }
    }
}